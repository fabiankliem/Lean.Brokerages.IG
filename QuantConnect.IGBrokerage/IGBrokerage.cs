/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Brokerages.IG.Api;
using QuantConnect.Brokerages.IG.Models;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Interfaces;
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// IG Markets brokerage implementation for live trading
    /// </summary>
    [BrokerageFactory(typeof(IGBrokerageFactory))]
    public partial class IGBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
    {
        #region Private Fields

        private readonly string _apiUrl;
        private readonly string _identifier;
        private readonly string _password;
        private readonly string _apiKey;
        private readonly string _accountId;
        private readonly IAlgorithm _algorithm;
        private readonly IDataAggregator _aggregator;

        // API Clients
        private IGRestApiClient _restClient;
        private IGLightstreamerClient _streamingClient;

        // Session tokens
        private string _cst;
        private string _securityToken;
        private string _lightstreamerEndpoint;

        // State management
        private volatile bool _isConnected;
        private readonly object _lock = new object();

        // Order tracking
        private readonly ConcurrentDictionary<string, Order> _ordersByBrokerId;
        private readonly ConcurrentDictionary<int, string> _brokerIdByOrderId;

        // Subscription management
        private readonly EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
        private readonly ConcurrentDictionary<Symbol, string> _subscribedEpics;

        // Rate limiting (IG: ~40 trade requests/min, ~60 non-trade/min)
        private readonly RateGate _tradingRateGate;
        private readonly RateGate _nonTradingRateGate;

        // Symbol mapper
        private readonly IGSymbolMapper _symbolMapper;

        #endregion

        #region Constructors

        /// <summary>
        /// Parameterless constructor for brokerage
        /// </summary>
        /// <remarks>This parameterless constructor is required for brokerages implementing <see cref="IDataQueueHandler"/></remarks>
        public IGBrokerage()
            : this(
                Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal"),
                Config.Get("ig-identifier"),
                Config.Get("ig-password"),
                Config.Get("ig-api-key"),
                Config.Get("ig-account-id"),
                null,
                Composer.Instance.GetPart<IDataAggregator>())
        {
        }

        /// <summary>
        /// Creates a new instance of the IGBrokerage class
        /// </summary>
        /// <param name="apiUrl">The IG API URL (demo or live)</param>
        /// <param name="identifier">IG account identifier (username)</param>
        /// <param name="password">IG account password</param>
        /// <param name="apiKey">IG API key</param>
        /// <param name="accountId">IG account ID (for multi-account users)</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <param name="aggregator">The data aggregator</param>
        public IGBrokerage(
            string apiUrl,
            string identifier,
            string password,
            string apiKey,
            string accountId,
            IAlgorithm algorithm,
            IDataAggregator aggregator)
            : base("IG")
        {
            _apiUrl = apiUrl;
            _identifier = identifier;
            _password = password;
            _apiKey = apiKey;
            _accountId = accountId;
            _algorithm = algorithm;
            _aggregator = aggregator;

            _ordersByBrokerId = new ConcurrentDictionary<string, Order>();
            _brokerIdByOrderId = new ConcurrentDictionary<int, string>();
            _subscribedEpics = new ConcurrentDictionary<Symbol, string>();

            // Rate limiting - IG limits: ~40 trading requests/min, ~60 non-trading/min
            _tradingRateGate = new RateGate(40, TimeSpan.FromMinutes(1));
            _nonTradingRateGate = new RateGate(60, TimeSpan.FromMinutes(1));

            // Symbol mapper for EPIC code translation
            _symbolMapper = new IGSymbolMapper();

            // Subscription manager
            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);
        }

        #endregion

        #region IBrokerage Properties

        /// <summary>
        /// Returns true if connected to the broker
        /// </summary>
        public override bool IsConnected => _isConnected;

        /// <summary>
        /// Specifies whether the brokerage will instantly update account balances
        /// </summary>
        public bool AccountInstantlyUpdated => true;

        #endregion

        #region Connection Methods

        /// <summary>
        /// Connects the client to the broker's remote servers
        /// </summary>
        public override void Connect()
        {
            if (_isConnected)
                return;

            lock (_lock)
            {
                if (_isConnected)
                    return;

                Log.Trace("IGBrokerage.Connect(): Connecting to IG Markets...");

                try
                {
                    // Initialize REST client
                    _restClient = new IGRestApiClient(_apiUrl, _apiKey);

                    // Authenticate with IG
                    _nonTradingRateGate.WaitToProceed();
                    var loginResponse = _restClient.Login(_identifier, _password);

                    _cst = loginResponse.Cst;
                    _securityToken = loginResponse.SecurityToken;
                    _lightstreamerEndpoint = loginResponse.LightstreamerEndpoint;

                    // Set session tokens for subsequent requests
                    _restClient.SetSessionTokens(_cst, _securityToken);

                    Log.Trace($"IGBrokerage.Connect(): Successfully authenticated. Account: {loginResponse.AccountId}");

                    // Switch to specified account if provided
                    var accountId = _accountId;
                    if (!string.IsNullOrEmpty(_accountId) && _accountId != loginResponse.AccountId)
                    {
                        _nonTradingRateGate.WaitToProceed();
                        _restClient.SwitchAccount(_accountId);
                        Log.Trace($"IGBrokerage.Connect(): Switched to account {_accountId}");
                    }
                    else
                    {
                        accountId = loginResponse.AccountId;
                    }

                    // Initialize Lightstreamer client for streaming
                    _streamingClient = new IGLightstreamerClient(
                        _lightstreamerEndpoint,
                        _cst,
                        _securityToken,
                        accountId
                    );

                    // Wire up event handlers
                    _streamingClient.OnPriceUpdate += HandlePriceUpdate;
                    _streamingClient.OnTradeUpdate += HandleTradeUpdate;
                    _streamingClient.OnAccountUpdate += HandleAccountUpdate;
                    _streamingClient.OnError += HandleStreamingError;
                    _streamingClient.OnDisconnect += HandleStreamingDisconnect;

                    // Connect to Lightstreamer
                    _streamingClient.Connect();

                    // Subscribe to trade and account updates
                    _streamingClient.SubscribeToTradeUpdates();
                    _streamingClient.SubscribeToAccountUpdates();

                    _isConnected = true;
                    Log.Trace("IGBrokerage.Connect(): Successfully connected to IG Markets");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.Connect(): Failed to connect: {ex.Message}");
                    _isConnected = false;
                    throw;
                }
            }
        }

        /// <summary>
        /// Disconnects the client from the broker's remote servers
        /// </summary>
        public override void Disconnect()
        {
            if (!_isConnected)
                return;

            lock (_lock)
            {
                if (!_isConnected)
                    return;

                Log.Trace("IGBrokerage.Disconnect(): Disconnecting from IG Markets...");

                try
                {
                    // Disconnect streaming client
                    if (_streamingClient != null)
                    {
                        _streamingClient.OnPriceUpdate -= HandlePriceUpdate;
                        _streamingClient.OnTradeUpdate -= HandleTradeUpdate;
                        _streamingClient.OnAccountUpdate -= HandleAccountUpdate;
                        _streamingClient.OnError -= HandleStreamingError;
                        _streamingClient.OnDisconnect -= HandleStreamingDisconnect;

                        _streamingClient.Disconnect();
                        _streamingClient.Dispose();
                        _streamingClient = null;
                    }

                    // Logout from REST API
                    if (_restClient != null)
                    {
                        _restClient.Logout();
                        _restClient.Dispose();
                        _restClient = null;
                    }

                    _isConnected = false;
                    Log.Trace("IGBrokerage.Disconnect(): Disconnected from IG Markets");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.Disconnect(): Error during disconnect: {ex.Message}");
                }
            }
        }

        #endregion

        #region IBrokerage Order Methods

        /// <summary>
        /// Gets all open orders on the account.
        /// </summary>
        /// <returns>The open orders returned from IG</returns>
        public override List<Order> GetOpenOrders()
        {
            Log.Trace("IGBrokerage.GetOpenOrders(): Fetching open orders...");

            try
            {
                _nonTradingRateGate.WaitToProceed();
                var workingOrders = _restClient.GetWorkingOrders();

                var orders = new List<Order>();

                foreach (var wo in workingOrders)
                {
                    var symbol = _symbolMapper.GetLeanSymbol(wo.Epic, SecurityType.Forex, Market.IG);
                    if (symbol == null)
                    {
                        Log.Trace($"IGBrokerage.GetOpenOrders(): Unable to map EPIC {wo.Epic} to LEAN symbol");
                        continue;
                    }

                    var direction = wo.Direction == "BUY" ? OrderDirection.Buy : OrderDirection.Sell;
                    var quantity = direction == OrderDirection.Buy ? wo.Size : -wo.Size;

                    Order order;
                    if (wo.OrderType == "LIMIT")
                    {
                        order = new LimitOrder(symbol, quantity, wo.Level, wo.CreatedDate);
                    }
                    else if (wo.OrderType == "STOP")
                    {
                        order = new StopMarketOrder(symbol, quantity, wo.Level, wo.CreatedDate);
                    }
                    else
                    {
                        Log.Trace($"IGBrokerage.GetOpenOrders(): Unsupported order type {wo.OrderType} for {symbol}");
                        continue;
                    }

                    // Store broker ID mapping
                    _ordersByBrokerId[wo.DealId] = order;
                    _brokerIdByOrderId[order.Id] = wo.DealId;

                    orders.Add(order);

                    Log.Trace($"IGBrokerage.GetOpenOrders(): {symbol} - {quantity} @ {wo.Level} ({wo.OrderType})");
                }

                return orders;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetOpenOrders(): Error: {ex.Message}");
                return new List<Order>();
            }
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            Log.Trace("IGBrokerage.GetAccountHoldings(): Fetching account holdings...");

            try
            {
                _nonTradingRateGate.WaitToProceed();
                var positions = _restClient.GetPositions();

                var holdings = new List<Holding>();

                foreach (var position in positions)
                {
                    var symbol = _symbolMapper.GetLeanSymbol(position.Epic, SecurityType.Forex, Market.IG);
                    if (symbol == null)
                    {
                        Log.Trace($"IGBrokerage.GetAccountHoldings(): Unable to map EPIC {position.Epic} to LEAN symbol");
                        continue;
                    }

                    var quantity = position.Direction == "BUY" ? position.Size : -position.Size;

                    holdings.Add(new Holding
                    {
                        Symbol = symbol,
                        Type = symbol.SecurityType,
                        Quantity = quantity,
                        AveragePrice = position.OpenLevel,
                        MarketPrice = position.CurrentLevel,
                        CurrencySymbol = position.Currency ?? "GBP"
                    });

                    Log.Trace($"IGBrokerage.GetAccountHoldings(): {symbol} - {quantity} @ {position.OpenLevel}");
                }

                return holdings;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetAccountHoldings(): Error: {ex.Message}");
                return new List<Holding>();
            }
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            Log.Trace("IGBrokerage.GetCashBalance(): Fetching cash balance...");

            try
            {
                _nonTradingRateGate.WaitToProceed();
                var accounts = _restClient.GetAccounts();

                var cashAmounts = new List<CashAmount>();

                // Find the current account or use the first one
                var account = accounts.FirstOrDefault(a => a.AccountId == _accountId) ?? accounts.FirstOrDefault();

                if (account != null)
                {
                    cashAmounts.Add(new CashAmount(
                        account.Balance.Available,
                        account.Currency ?? "GBP"
                    ));

                    Log.Trace($"IGBrokerage.GetCashBalance(): Account {account.AccountId} - " +
                             $"{account.Balance.Available} {account.Currency} available");
                }

                return cashAmounts;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetCashBalance(): Error: {ex.Message}");
                return new List<CashAmount>();
            }
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            Log.Trace($"IGBrokerage.PlaceOrder(): Placing order {order.Id} for {order.Symbol}");

            try
            {
                var epic = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                if (string.IsNullOrEmpty(epic))
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = $"Unable to map symbol {order.Symbol} to IG EPIC"
                    });
                    return false;
                }

                var request = new IGPlaceOrderRequest
                {
                    Epic = epic,
                    Direction = order.Quantity > 0 ? "BUY" : "SELL",
                    Size = Math.Abs(order.Quantity),
                    CurrencyCode = "GBP",
                    Expiry = "DFB", // Daily funded bet (CFD)
                    ForceOpen = true,
                    GuaranteedStop = false
                };

                // Set order type specific parameters
                if (order.Type == OrderType.Market || order.Type == OrderType.MarketOnOpen)
                {
                    request.OrderType = "MARKET";
                }
                else if (order.Type == OrderType.Limit)
                {
                    var limitOrder = (LimitOrder)order;
                    request.OrderType = "LIMIT";
                    request.Level = limitOrder.LimitPrice;
                }
                else if (order.Type == OrderType.StopMarket)
                {
                    var stopOrder = (StopMarketOrder)order;
                    request.OrderType = "STOP";
                    request.Level = stopOrder.StopPrice;
                }
                else if (order.Type == OrderType.StopLimit)
                {
                    var stopLimitOrder = (StopLimitOrder)order;
                    request.OrderType = "LIMIT";
                    request.Level = stopLimitOrder.LimitPrice;
                }
                else
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = $"Unsupported order type: {order.Type}"
                    });
                    return false;
                }

                _tradingRateGate.WaitToProceed();
                var response = _restClient.PlaceOrder(request);

                if (response.Success)
                {
                    // Store broker ID mapping
                    _brokerIdByOrderId[order.Id] = response.DealReference;
                    _ordersByBrokerId[response.DealReference] = order;

                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Submitted
                    });

                    Log.Trace($"IGBrokerage.PlaceOrder(): Order {order.Id} submitted. DealRef: {response.DealReference}");
                    return true;
                }
                else
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = response.Reason ?? "Order placement failed"
                    });
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.PlaceOrder(): Error placing order {order.Id}: {ex.Message}");
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = OrderStatus.Invalid,
                    Message = ex.Message
                });
                return false;
            }
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            Log.Trace($"IGBrokerage.UpdateOrder(): Updating order {order.Id}");

            try
            {
                if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
                {
                    Log.Error($"IGBrokerage.UpdateOrder(): No broker ID found for order {order.Id}");
                    return false;
                }

                var request = new IGUpdateOrderRequest
                {
                    DealId = dealId
                };

                // Update level based on order type
                if (order.Type == OrderType.Limit)
                {
                    var limitOrder = (LimitOrder)order;
                    request.Level = limitOrder.LimitPrice;
                }
                else if (order.Type == OrderType.StopMarket)
                {
                    var stopOrder = (StopMarketOrder)order;
                    request.Level = stopOrder.StopPrice;
                }
                else if (order.Type == OrderType.StopLimit)
                {
                    var stopLimitOrder = (StopLimitOrder)order;
                    request.Level = stopLimitOrder.LimitPrice;
                }
                else
                {
                    Log.Error($"IGBrokerage.UpdateOrder(): Cannot update order type {order.Type}");
                    return false;
                }

                _tradingRateGate.WaitToProceed();
                var response = _restClient.UpdateOrder(request);

                if (response.Success)
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.UpdateSubmitted
                    });

                    Log.Trace($"IGBrokerage.UpdateOrder(): Order {order.Id} updated. DealRef: {response.DealReference}");
                    return true;
                }
                else
                {
                    Log.Error($"IGBrokerage.UpdateOrder(): Update failed: {response.Reason}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.UpdateOrder(): Error updating order {order.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            Log.Trace($"IGBrokerage.CancelOrder(): Canceling order {order.Id}");

            try
            {
                if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
                {
                    Log.Error($"IGBrokerage.CancelOrder(): No broker ID found for order {order.Id}");
                    return false;
                }

                _tradingRateGate.WaitToProceed();
                var response = _restClient.CancelOrder(dealId);

                if (response.Success)
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Canceled
                    });

                    // Remove from tracking dictionaries
                    _brokerIdByOrderId.TryRemove(order.Id, out _);
                    _ordersByBrokerId.TryRemove(dealId, out _);

                    Log.Trace($"IGBrokerage.CancelOrder(): Order {order.Id} canceled. DealRef: {response.DealReference}");
                    return true;
                }
                else
                {
                    Log.Error($"IGBrokerage.CancelOrder(): Cancellation failed: {response.Reason}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.CancelOrder(): Error canceling order {order.Id}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IDataQueueHandler

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        /// <param name="dataConfig">defines the parameters to subscribe to a data feed</param>
        /// <param name="newDataAvailableHandler">handler to be fired on new data available</param>
        /// <returns>The new enumerator for this subscription request</returns>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            if (!CanSubscribe(dataConfig.Symbol))
            {
                return null;
            }

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        /// <param name="dataConfig">Subscription config to be removed</param>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        /// <param name="job">Job we're subscribing for</param>
        public void SetJob(LiveNodePacket job)
        {
            // Initialize from job packet if needed
            // This is called when brokerage is used as IDataQueueHandler without being the brokerage
        }

        #endregion

        #region IDataQueueUniverseProvider

        /// <summary>
        /// Method returns a collection of Symbols that are available at the data source.
        /// </summary>
        /// <param name="symbol">Symbol to lookup</param>
        /// <param name="includeExpired">Include expired contracts</param>
        /// <param name="securityCurrency">Expected security currency(if any)</param>
        /// <returns>Enumerable of Symbols, that are associated with the provided Symbol</returns>
        public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
        {
            if (!_isConnected || _restClient == null)
            {
                return Enumerable.Empty<Symbol>();
            }

            try
            {
                // Search for markets using the symbol value
                var searchTerm = symbol.Value;

                _nonTradingRateGate.WaitToProceed();
                var markets = _restClient.SearchMarkets(searchTerm);

                var symbols = new List<Symbol>();

                foreach (var market in markets)
                {
                    try
                    {
                        // Determine security type based on instrument type
                        SecurityType securityType;

                        switch (market.InstrumentType?.ToUpperInvariant())
                        {
                            case "CURRENCIES":
                                securityType = SecurityType.Forex;
                                break;
                            case "INDICES":
                                securityType = SecurityType.Index;
                                break;
                            case "COMMODITIES":
                                securityType = SecurityType.Cfd;
                                break;
                            case "SHARES":
                                securityType = SecurityType.Equity;
                                break;
                            case "CRYPTOCURRENCIES":
                                securityType = SecurityType.Crypto;
                                break;
                            default:
                                // Default to CFD for unknown types
                                securityType = SecurityType.Cfd;
                                break;
                        }

                        // Try to map the EPIC to a LEAN symbol
                        var leanSymbol = _symbolMapper.GetLeanSymbol(market.Epic, securityType, Market.IG);

                        if (leanSymbol != null)
                        {
                            symbols.Add(leanSymbol);
                        }
                        else
                        {
                            // If no mapping exists, create a generic symbol
                            // Use the instrument name as the ticker
                            var ticker = market.InstrumentName?.Replace(" ", "") ?? market.Epic;
                            leanSymbol = Symbol.Create(ticker, securityType, Market.IG);
                            symbols.Add(leanSymbol);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"IGBrokerage.LookupSymbols(): Error processing market {market.Epic}: {ex.Message}");
                    }
                }

                Log.Trace($"IGBrokerage.LookupSymbols(): Found {symbols.Count} symbols for search term '{searchTerm}'");
                return symbols;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.LookupSymbols(): Error searching markets: {ex.Message}");
                return Enumerable.Empty<Symbol>();
            }
        }

        /// <summary>
        /// Returns whether selection can take place or not.
        /// </summary>
        /// <returns>True if selection can take place</returns>
        public bool CanPerformSelection()
        {
            return _isConnected;
        }

        #endregion

        // History Provider implementation moved to IGBrokerage.History.cs partial class

        #region Private Methods

        /// <summary>
        /// Determines if we can subscribe to the specified symbol
        /// </summary>
        private bool CanSubscribe(Symbol symbol)
        {
            // Cannot subscribe to universe symbols or canonical symbols
            if (symbol.Value.IndexOfInvariant("universe", true) != -1 || symbol.IsCanonical())
            {
                return false;
            }

            // Check if security type is supported
            var securityType = symbol.SecurityType;
            return securityType == SecurityType.Forex ||
                   securityType == SecurityType.Cfd ||
                   securityType == SecurityType.Crypto ||
                   securityType == SecurityType.Index ||
                   securityType == SecurityType.Equity;
        }

        /// <summary>
        /// Adds the specified symbols to the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be added</param>
        private bool Subscribe(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                var epic = _symbolMapper.GetBrokerageSymbol(symbol);
                if (!string.IsNullOrEmpty(epic))
                {
                    _subscribedEpics[symbol] = epic;

                    // Subscribe to Lightstreamer price updates for this EPIC
                    if (_streamingClient != null)
                    {
                        _streamingClient.SubscribeToPrices(epic);
                    }

                    Log.Trace($"IGBrokerage.Subscribe(): Subscribed to {symbol} (EPIC: {epic})");
                }
            }
            return true;
        }

        /// <summary>
        /// Removes the specified symbols from the subscription
        /// </summary>
        /// <param name="symbols">The symbols to be removed</param>
        private bool Unsubscribe(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (_subscribedEpics.TryRemove(symbol, out var epic))
                {
                    // Unsubscribe from Lightstreamer price updates for this EPIC
                    if (_streamingClient != null)
                    {
                        _streamingClient.UnsubscribeFromPrices(epic);
                    }

                    Log.Trace($"IGBrokerage.Unsubscribe(): Unsubscribed from {symbol} (EPIC: {epic})");
                }
            }
            return true;
        }

        #endregion

        #region Streaming Event Handlers

        /// <summary>
        /// Handles price updates from Lightstreamer
        /// </summary>
        private void HandlePriceUpdate(object sender, IGPriceUpdateEventArgs e)
        {
            try
            {
                // Find the LEAN symbol for this EPIC
                var symbol = _subscribedEpics.FirstOrDefault(kvp => kvp.Value == e.Epic).Key;
                if (symbol == null)
                {
                    return;
                }

                // Create tick data from price update
                if (e.Bid.HasValue && e.Ask.HasValue)
                {
                    var tick = new Tick
                    {
                        Symbol = symbol,
                        Time = DateTime.UtcNow,
                        Value = (e.Bid.Value + e.Ask.Value) / 2,
                        BidPrice = e.Bid.Value,
                        AskPrice = e.Ask.Value,
                        TickType = TickType.Quote
                    };

                    lock (_lock)
                    {
                        _aggregator.Update(tick);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.HandlePriceUpdate(): Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles trade/order updates from Lightstreamer
        /// </summary>
        private void HandleTradeUpdate(object sender, IGTradeUpdateEventArgs e)
        {
            try
            {
                Log.Trace($"IGBrokerage.HandleTradeUpdate(): DealId={e.DealId}, Status={e.Status}, " +
                         $"Price={e.FilledPrice}, Size={e.FilledSize}");

                // Find the order by broker ID
                if (!_ordersByBrokerId.TryGetValue(e.DealId, out var order))
                {
                    Log.Warning($"IGBrokerage.HandleTradeUpdate(): Order not found for DealId {e.DealId}");
                    return;
                }

                var status = MapIGStatusToOrderStatus(e.Status);

                var orderEvent = new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = status,
                    Message = e.Reason
                };

                if (status == OrderStatus.Filled || status == OrderStatus.PartiallyFilled)
                {
                    orderEvent.FillPrice = e.FilledPrice ?? 0;
                    orderEvent.FillQuantity = e.FilledSize ?? 0;
                }

                // Remove from tracking if filled or cancelled
                if (status == OrderStatus.Filled || status == OrderStatus.Canceled)
                {
                    _ordersByBrokerId.TryRemove(e.DealId, out _);
                    _brokerIdByOrderId.TryRemove(order.Id, out _);
                }

                OnOrderEvent(orderEvent);
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.HandleTradeUpdate(): Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles account balance updates from Lightstreamer
        /// </summary>
        private void HandleAccountUpdate(object sender, IGAccountUpdateEventArgs e)
        {
            try
            {
                Log.Trace($"IGBrokerage.HandleAccountUpdate(): Balance={e.Balance} {e.Currency}, " +
                         $"Available={e.AvailableCash}, PnL={e.PnL}");

                // Notify algorithm of account changes if needed
                OnAccountChanged(new AccountEvent(e.Currency, e.AvailableCash));
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.HandleAccountUpdate(): Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles streaming errors from Lightstreamer
        /// </summary>
        private void HandleStreamingError(object sender, IGStreamingErrorEventArgs e)
        {
            Log.Error($"IGBrokerage.HandleStreamingError(): Code={e.Code}, Message={e.Message}");
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, e.Code, e.Message));
        }

        /// <summary>
        /// Handles disconnection from Lightstreamer
        /// </summary>
        private void HandleStreamingDisconnect(object sender, EventArgs e)
        {
            Log.Warning("IGBrokerage.HandleStreamingDisconnect(): Disconnected from Lightstreamer");
            _isConnected = false;
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Disconnect, -1, "Disconnected from streaming"));
        }

        /// <summary>
        /// Maps IG order status to LEAN OrderStatus
        /// </summary>
        private OrderStatus MapIGStatusToOrderStatus(string igStatus)
        {
            switch (igStatus?.ToUpperInvariant())
            {
                case "ACCEPTED":
                case "OPEN":
                    return OrderStatus.Submitted;
                case "AMENDED":
                    return OrderStatus.UpdateSubmitted;
                case "DELETED":
                    return OrderStatus.Canceled;
                case "REJECTED":
                    return OrderStatus.Invalid;
                case "FILLED":
                    return OrderStatus.Filled;
                case "PARTIALLY_FILLED":
                    return OrderStatus.PartiallyFilled;
                default:
                    Log.Warning($"IGBrokerage.MapIGStatusToOrderStatus(): Unknown status '{igStatus}'");
                    return OrderStatus.None;
            }
        }

        #endregion
    }
}
