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

using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using QuantConnect.Brokerages.IG.Api;
using QuantConnect.Brokerages.IG.Models;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Interfaces;
using QuantConnect.Orders.Fees;
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
        private IDataAggregator _aggregator;

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

        // Concurrent message handler for thread-safe order operations
        private readonly BrokerageConcurrentMessageHandler<IGTradeUpdateEventArgs> _messageHandler;

        // ReSubscription infrastructure for automatic reconnection
        private CancellationTokenSource _cancellationTokenSource;
        private Task _reconnectionMonitorTask;
        private Task _restPollingTask;

        // IG instrument conversion cache: maps EPIC -> (PipValue, ContractSize)
        // IG returns forex prices in "points" (e.g., EURUSD 11792.2 = 1.17922 exchange rate)
        // PipValue converts IG points to standard price: standard = igPrice * pipValue
        // ContractSize converts LEAN quantity to IG contracts: igSize = leanQty / contractSize
        private readonly ConcurrentDictionary<string, (decimal PipValue, decimal ContractSize)> _instrumentConversion;

        // Supported security types for validation
        private static readonly HashSet<SecurityType> _supportedSecurityTypes = new HashSet<SecurityType>
        {
            SecurityType.Forex,
            SecurityType.Index,
            SecurityType.Cfd,
            SecurityType.Crypto,
            SecurityType.Equity
        };

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
            _instrumentConversion = new ConcurrentDictionary<string, (decimal PipValue, decimal ContractSize)>();

            // Rate limiting - IG limits: ~40 trading requests/min, ~60 non-trading/min
            _tradingRateGate = new RateGate(40, TimeSpan.FromMinutes(1));
            _nonTradingRateGate = new RateGate(60, TimeSpan.FromMinutes(1));

            // Symbol mapper for EPIC code translation
            _symbolMapper = new IGSymbolMapper();

            // Subscription manager
            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);

            // Initialize concurrent message handler for thread-safe order operations
            _messageHandler = new BrokerageConcurrentMessageHandler<IGTradeUpdateEventArgs>(ProcessTradeUpdate);

            // Initialize cancellation token for reconnection monitoring
            _cancellationTokenSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Creates a new instance of the IGBrokerage class for testing
        /// </summary>
        /// <param name="apiUrl">The IG API URL (demo or live)</param>
        /// <param name="apiKey">IG API key</param>
        /// <param name="identifier">IG account identifier (username)</param>
        /// <param name="password">IG account password</param>
        /// <param name="accountId">IG account ID (for multi-account users)</param>
        /// <param name="environment">Environment (demo or live)</param>
        /// <param name="orderProvider">Order provider</param>
        /// <param name="securityProvider">Security provider</param>
        public IGBrokerage(
            string apiUrl,
            string apiKey,
            string identifier,
            string password,
            string accountId,
            string environment,
            IOrderProvider orderProvider,
            ISecurityProvider securityProvider)
            : this(apiUrl, identifier, password, apiKey, accountId, null, null)
        {
            // Store providers for testing
            // Note: orderProvider and securityProvider are handled by base class
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

        #region Internal/Testing Properties and Methods

        /// <summary>
        /// Symbol mapper for testing purposes
        /// </summary>
        internal IGSymbolMapper SymbolMapper => _symbolMapper;

        /// <summary>
        /// Gets current market data for the specified EPIC
        /// Internal method for testing purposes
        /// </summary>
        /// <param name="epic">IG EPIC code</param>
        /// <returns>Market data with bid/offer prices</returns>
        internal dynamic GetMarketData(string epic)
        {
            _nonTradingRateGate.WaitToProceed();

            var response = _restClient.GetMarketDetails(epic);
            var snapshot = response["snapshot"];

            // Get conversion info for this EPIC
            var conversion = GetInstrumentConversion(epic);
            var pv = conversion.PipValue;

            return new
            {
                Bid = (snapshot["bid"]?.Value<decimal>() ?? 0) * pv,
                Offer = (snapshot["offer"]?.Value<decimal>() ?? 0) * pv,
                High = (snapshot["high"]?.Value<decimal>() ?? 0) * pv,
                Low = (snapshot["low"]?.Value<decimal>() ?? 0) * pv
            };
        }

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

                    // Initialize Lightstreamer client for streaming (optional - REST polling is the fallback)
                    try
                    {
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
                    }
                    catch (Exception lsEx)
                    {
                        Log.Trace($"IGBrokerage.Connect(): Lightstreamer unavailable ({lsEx.GetType().Name}), using REST polling for market data");
                        _streamingClient = null;
                    }

                    _isConnected = true;
                    Log.Trace("IGBrokerage.Connect(): Successfully connected to IG Markets");

                    // Validate subscriptions if algorithm is available
                    ValidateSubscriptions();

                    // Start reconnection monitoring
                    _reconnectionMonitorTask = Task.Run(() =>
                        MonitorOrderConnection(_cancellationTokenSource.Token));

                    // Start REST polling fallback for price data (in case Lightstreamer fails)
                    _restPollingTask = Task.Run(() =>
                        PollPricesViaRest(_cancellationTokenSource.Token));

                    Log.Trace("IGBrokerage.Connect(): Started connection monitoring and REST price polling");
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

            // Cancel reconnection monitoring
            _cancellationTokenSource?.Cancel();

            // Wait for monitoring task to complete
            if (_reconnectionMonitorTask != null)
            {
                try
                {
                    _reconnectionMonitorTask.Wait(TimeSpan.FromSeconds(5));
                    Log.Trace("IGBrokerage.Disconnect(): Connection monitoring stopped");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.Disconnect(): Error stopping monitoring: {ex.Message}");
                }
            }

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

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources
        /// </summary>
        public override void Dispose()
        {
            // Disconnect if still connected
            Disconnect();

            // Dispose cancellation token source
            _cancellationTokenSource?.Dispose();

            // Dispose message handler
            _messageHandler?.Dispose();

            // Dispose rate gates
            _tradingRateGate?.Dispose();
            _nonTradingRateGate?.Dispose();

            base.Dispose();
        }

        /// <summary>
        /// Initializes the brokerage connection with comprehensive validation
        /// </summary>
        /// <remarks>
        /// This method consolidates connection and validation logic into a single call.
        /// Recommended usage pattern: Call Initialize() instead of Connect() for production code.
        /// </remarks>
        public void Initialize()
        {
            if (_isConnected)
            {
                Log.Trace("IGBrokerage.Initialize(): Already initialized and connected");
                return;
            }

            Log.Trace("IGBrokerage.Initialize(): Initializing IG Markets brokerage...");

            // Validate required configuration
            if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_identifier) || string.IsNullOrEmpty(_password))
            {
                throw new InvalidOperationException(
                    "IGBrokerage.Initialize(): Missing required credentials. " +
                    "Ensure ig-api-key, ig-identifier, and ig-password are configured."
                );
            }

            if (string.IsNullOrEmpty(_apiUrl))
            {
                throw new InvalidOperationException(
                    "IGBrokerage.Initialize(): Missing API URL. " +
                    "Ensure ig-api-url is configured."
                );
            }

            // Connect to IG Markets
            try
            {
                Connect();
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.Initialize(): Connection failed: {ex.Message}");
                throw new Exception($"Failed to initialize IG Markets brokerage: {ex.Message}", ex);
            }

            // Verify connection was successful
            if (!IsConnected)
            {
                throw new Exception("IGBrokerage.Initialize(): Connection succeeded but IsConnected returned false");
            }

            Log.Trace("IGBrokerage.Initialize(): Initialization complete. Ready for trading.");
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

                    // Get conversion info for this EPIC
                    var conversion = GetInstrumentConversion(wo.Epic);

                    var direction = wo.Direction == "BUY" ? OrderDirection.Buy : OrderDirection.Sell;
                    // Convert IG contracts to LEAN base currency units
                    var quantity = direction == OrderDirection.Buy
                        ? wo.Size * conversion.ContractSize
                        : -wo.Size * conversion.ContractSize;
                    // Convert IG points to standard price
                    var level = ConvertIGPriceToLean(wo.Level, conversion.PipValue);

                    Order order;
                    if (wo.OrderType == "LIMIT")
                    {
                        order = new LimitOrder(symbol, quantity, level, wo.CreatedDate);
                    }
                    else if (wo.OrderType == "STOP")
                    {
                        order = new StopMarketOrder(symbol, quantity, level, wo.CreatedDate);
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

                    // Get conversion info for this EPIC
                    var conversion = GetInstrumentConversion(position.Epic);

                    // Convert IG contracts to LEAN base currency units
                    var leanQuantity = position.Direction == "BUY"
                        ? position.Size * conversion.ContractSize
                        : -position.Size * conversion.ContractSize;

                    holdings.Add(new Holding
                    {
                        Symbol = symbol,
                        Quantity = leanQuantity,
                        AveragePrice = ConvertIGPriceToLean(position.OpenLevel, conversion.PipValue),
                        MarketPrice = ConvertIGPriceToLean(position.CurrentLevel, conversion.PipValue),
                        CurrencySymbol = position.Currency ?? "USD"
                    });

                    Log.Trace($"IGBrokerage.GetAccountHoldings(): {symbol} - {leanQuantity} @ {ConvertIGPriceToLean(position.OpenLevel, conversion.PipValue)}");
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
                var result = false;
                _messageHandler.WithLockedStream(() =>
                {
                    result = PlaceOrderInternal(order);
                });
                return result;
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
        /// Internal implementation of order placement (called within locked stream)
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        private bool PlaceOrderInternal(Order order)
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

                // Get conversion info for quantity and price conversion
                var conversion = GetInstrumentConversion(epic);

                // Convert LEAN quantity (base currency units) to IG contracts
                var igSize = Math.Abs(order.Quantity) / conversion.ContractSize;

                var request = new IGPlaceOrderRequest
                {
                    Epic = epic,
                    Direction = order.Quantity > 0 ? "BUY" : "SELL",
                    Size = igSize,
                    CurrencyCode = "USD",
                    Expiry = "-", // No expiry for CFD
                    ForceOpen = true,
                    GuaranteedStop = false
                };

                Log.Trace($"IGBrokerage.PlaceOrderInternal(): LEAN qty={order.Quantity}, IG size={igSize} contracts (contractSize={conversion.ContractSize})");

                // Set order type specific parameters
                decimal? entryPrice = null;
                if (order.Type == OrderType.Market || order.Type == OrderType.MarketOnOpen)
                {
                    request.OrderType = "MARKET";
                    // For market orders, entry price will be current market price (we'll get it below)
                }
                else if (order.Type == OrderType.Limit)
                {
                    var limitOrder = (LimitOrder)order;
                    request.OrderType = "LIMIT";
                    // Convert LEAN price to IG points
                    request.Level = ConvertLeanPriceToIG(limitOrder.LimitPrice, conversion.PipValue);
                    entryPrice = limitOrder.LimitPrice;
                }
                else if (order.Type == OrderType.StopMarket)
                {
                    var stopOrder = (StopMarketOrder)order;
                    request.OrderType = "STOP";
                    request.Level = ConvertLeanPriceToIG(stopOrder.StopPrice, conversion.PipValue);
                    entryPrice = stopOrder.StopPrice;
                }
                else if (order.Type == OrderType.StopLimit)
                {
                    var stopLimitOrder = (StopLimitOrder)order;
                    request.OrderType = "LIMIT";
                    request.Level = ConvertLeanPriceToIG(stopLimitOrder.LimitPrice, conversion.PipValue);
                    entryPrice = stopLimitOrder.LimitPrice;
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

                // Parse stop loss and take profit from order tag
                // Expected format: "SL:1.1000;TP:1.2000" or "SL:100;TP:200" (points distance)
                decimal? stopLossPrice = null;
                decimal? takeProfitPrice = null;

                if (!string.IsNullOrEmpty(order.Tag))
                {
                    ParseStopLossAndTakeProfit(order.Tag, out stopLossPrice, out takeProfitPrice);
                }

                // For market orders, we need the current price to calculate distances
                if (entryPrice == null && (stopLossPrice.HasValue || takeProfitPrice.HasValue))
                {
                    // Get current market price from the security
                    var security = _algorithm?.Securities[order.Symbol];
                    if (security != null)
                    {
                        entryPrice = order.Direction == OrderDirection.Buy ? security.AskPrice : security.BidPrice;
                    }
                }

                // Calculate and set stop loss distance
                if (stopLossPrice.HasValue && entryPrice.HasValue)
                {
                    var stopDistance = CalculatePriceDistance(entryPrice.Value, stopLossPrice.Value, order.Direction);
                    if (stopDistance > 0)
                    {
                        request.StopDistance = stopDistance;
                        Log.Trace($"IGBrokerage.PlaceOrder(): Setting stop loss at distance {stopDistance} points");
                    }
                }

                // Calculate and set take profit distance
                if (takeProfitPrice.HasValue && entryPrice.HasValue)
                {
                    var limitDistance = CalculatePriceDistance(entryPrice.Value, takeProfitPrice.Value,
                        order.Direction == OrderDirection.Buy ? OrderDirection.Sell : OrderDirection.Buy);
                    if (limitDistance > 0)
                    {
                        request.LimitDistance = limitDistance;
                        Log.Trace($"IGBrokerage.PlaceOrder(): Setting take profit at distance {limitDistance} points");
                    }
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

                    Log.Trace($"IGBrokerage.PlaceOrderInternal(): Order {order.Id} submitted. DealRef: {response.DealReference}");

                    // Poll for deal confirmation via REST (since Lightstreamer may not be available)
                    PollDealConfirmation(order, response.DealReference, conversion.PipValue, conversion.ContractSize);

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
                var result = false;
                _messageHandler.WithLockedStream(() =>
                {
                    result = UpdateOrderInternal(order);
                });
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.UpdateOrder(): Error updating order {order.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Internal implementation of order update (called within locked stream)
        /// </summary>
        /// <param name="order">The order to update</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        private bool UpdateOrderInternal(Order order)
        {
            if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
            {
                Log.Error($"IGBrokerage.UpdateOrderInternal(): No broker ID found for order {order.Id}");
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
                Log.Error($"IGBrokerage.UpdateOrderInternal(): Cannot update order type {order.Type}");
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

                Log.Trace($"IGBrokerage.UpdateOrderInternal(): Order {order.Id} updated. DealRef: {response.DealReference}");
                return true;
            }
            else
            {
                Log.Error($"IGBrokerage.UpdateOrderInternal(): Update failed: {response.Reason}");
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
                var result = false;
                _messageHandler.WithLockedStream(() =>
                {
                    result = CancelOrderInternal(order);
                });
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.CancelOrder(): Error canceling order {order.Id}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Internal implementation of order cancellation (called within locked stream)
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        private bool CancelOrderInternal(Order order)
        {
            if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
            {
                Log.Error($"IGBrokerage.CancelOrderInternal(): No broker ID found for order {order.Id}");
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

                Log.Trace($"IGBrokerage.CancelOrderInternal(): Order {order.Id} canceled. DealRef: {response.DealReference}");
                return true;
            }
            else
            {
                Log.Error($"IGBrokerage.CancelOrderInternal(): Cancellation failed: {response.Reason}");
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

            // Lazy-initialize aggregator if needed
            if (_aggregator == null)
            {
                _aggregator = Composer.Instance.GetPart<IDataAggregator>()
                    ?? Composer.Instance.GetExportedValueByTypeName<IDataAggregator>("AggregationManager");
                if (_aggregator == null)
                {
                    Log.Error("IGBrokerage.Subscribe(): Data aggregator is not available");
                    return null;
                }
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
            _aggregator?.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        /// <param name="job">Job we're subscribing for</param>
        public void SetJob(LiveNodePacket job)
        {
            if (job == null)
            {
                throw new ArgumentNullException(nameof(job));
            }

            Log.Trace($"IGBrokerage.SetJob(): Initializing data queue handler for algorithm {job.AlgorithmId}");
            Log.Trace($"IGBrokerage.SetJob(): Deploy ID: {job.DeployId}, Data Provider: {job.DataQueueHandler}");

            // Lazy-initialize aggregator if not set (e.g., when created via parameterless constructor before Composer is fully ready)
            if (_aggregator == null)
            {
                _aggregator = Composer.Instance.GetPart<IDataAggregator>();
                if (_aggregator == null)
                {
                    Log.Trace("IGBrokerage.SetJob(): Data aggregator not available yet, will initialize on first subscription");
                }
            }

            // Log brokerage configuration if present
            if (job.BrokerageData != null && job.BrokerageData.Count > 0)
            {
                Log.Trace($"IGBrokerage.SetJob(): Loaded {job.BrokerageData.Count} brokerage configuration items");
            }

            Log.Trace("IGBrokerage.SetJob(): Data queue handler ready for subscriptions");
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
        /// Fetches and caches the instrument conversion info (pip value and contract size) for an EPIC.
        /// IG returns forex prices in "points" format (e.g., EURUSD 11792.2 = 1.17922 exchange rate).
        /// This method fetches the market details to determine the conversion factors.
        /// </summary>
        /// <param name="epic">The IG EPIC code</param>
        /// <returns>Tuple of (PipValue, ContractSize) for price/quantity conversion</returns>
        internal (decimal PipValue, decimal ContractSize) GetInstrumentConversion(string epic)
        {
            if (_instrumentConversion.TryGetValue(epic, out var cached))
            {
                return cached;
            }

            try
            {
                _nonTradingRateGate.WaitToProceed();
                var marketDetails = _restClient.GetMarketDetails(epic);
                var instrument = marketDetails["instrument"];

                // Parse pipValue from onePipMeans (e.g., "0.0001 USD/EUR")
                decimal pipValue = 1m;
                var onePipMeans = instrument?["onePipMeans"]?.ToString();
                if (!string.IsNullOrEmpty(onePipMeans))
                {
                    var parts = onePipMeans.Split(' ');
                    if (parts.Length > 0 && decimal.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var pv))
                    {
                        pipValue = pv;
                    }
                }

                // Parse contractSize (e.g., "10000")
                decimal contractSize = 1m;
                var contractSizeStr = instrument?["contractSize"]?.ToString();
                if (!string.IsNullOrEmpty(contractSizeStr) &&
                    decimal.TryParse(contractSizeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var cs))
                {
                    contractSize = cs;
                }

                var result = (pipValue, contractSize);
                _instrumentConversion[epic] = result;

                Log.Trace($"IGBrokerage.GetInstrumentConversion(): {epic} - PipValue={pipValue}, ContractSize={contractSize}");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetInstrumentConversion(): Error for {epic}: {ex.Message}");
                return (1m, 1m); // Default: no conversion
            }
        }

        /// <summary>
        /// Converts an IG points price to a standard LEAN price
        /// </summary>
        private decimal ConvertIGPriceToLean(decimal igPrice, decimal pipValue)
        {
            return igPrice * pipValue;
        }

        /// <summary>
        /// Converts a standard LEAN price to IG points price
        /// </summary>
        private decimal ConvertLeanPriceToIG(decimal leanPrice, decimal pipValue)
        {
            if (pipValue == 0) return leanPrice;
            return leanPrice / pipValue;
        }

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

            // Use centralized validation
            return ValidateSubscription(symbol, symbol.SecurityType, Resolution.Minute, TickType.Trade);
        }

        /// <summary>
        /// Validates that the brokerage supports the requested subscription
        /// Called during initialization to catch configuration errors early
        /// </summary>
        /// <param name="symbol">Symbol to validate</param>
        /// <param name="securityType">Security type</param>
        /// <param name="resolution">Data resolution</param>
        /// <param name="tickType">Tick type</param>
        /// <returns>True if subscription is valid</returns>
        private bool ValidateSubscription(Symbol symbol, SecurityType securityType, Resolution resolution, TickType tickType)
        {
            // Validate security type is supported
            if (!_supportedSecurityTypes.Contains(securityType))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedSecurityType",
                    $"IG Markets does not support {securityType}. Supported types: {string.Join(", ", _supportedSecurityTypes)}"));
                return false;
            }

            // Validate symbol can be mapped
            var epic = _symbolMapper.GetBrokerageSymbol(symbol);
            if (string.IsNullOrEmpty(epic))
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnmappedSymbol",
                    $"Symbol {symbol} cannot be mapped to IG EPIC. Use symbol mapper or SearchMarkets API."));
                return false;
            }

            // Validate resolution is supported for history
            if (resolution == Resolution.Tick)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedResolution",
                    $"IG Markets does not support {resolution} historical data. Use Second or higher."));
                return false;
            }

            // Validate tick type combinations
            if (tickType == TickType.OpenInterest)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedTickType",
                    $"IG Markets does not support OpenInterest data."));
                return false;
            }

            // Forex and CFD typically support both quote and trade
            // Indices typically only support trade
            if (securityType == SecurityType.Index && tickType == TickType.Quote)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedTickType",
                    $"Index {symbol} may not support quote data. Try TickType.Trade."));
            }

            return true;
        }

        /// <summary>
        /// Validates that all current subscriptions are supported by the brokerage
        /// Should be called during initialization
        /// </summary>
        private void ValidateSubscriptions()
        {
            if (_algorithm == null)
                return;

            Log.Trace("IGBrokerage.ValidateSubscriptions(): Validating algorithm subscriptions...");

            var subscriptions = _algorithm.SubscriptionManager.Subscriptions;
            var invalidCount = 0;

            foreach (var subscription in subscriptions)
            {
                var symbol = subscription.Symbol;

                if (!ValidateSubscription(symbol, subscription.SecurityType, subscription.Resolution, subscription.TickType))
                {
                    invalidCount++;
                }
            }

            if (invalidCount > 0)
            {
                Log.Trace($"IGBrokerage.ValidateSubscriptions(): Found {invalidCount} potentially invalid subscriptions");
            }
            else
            {
                Log.Trace("IGBrokerage.ValidateSubscriptions(): All subscriptions validated successfully");
            }
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

                    // Pre-fetch and cache instrument conversion info
                    if (_restClient != null)
                    {
                        GetInstrumentConversion(epic);
                    }

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
                    // Get conversion info for this EPIC
                    _instrumentConversion.TryGetValue(e.Epic, out var conversion);
                    var pipValue = conversion.PipValue != 0 ? conversion.PipValue : 1m;

                    // Convert IG points to standard prices
                    var bidPrice = ConvertIGPriceToLean(e.Bid.Value, pipValue);
                    var askPrice = ConvertIGPriceToLean(e.Ask.Value, pipValue);

                    var tick = new Tick
                    {
                        Symbol = symbol,
                        Time = DateTime.UtcNow,
                        Value = (bidPrice + askPrice) / 2,
                        BidPrice = bidPrice,
                        AskPrice = askPrice,
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
        /// Processes trade/order update events (called by message handler)
        /// </summary>
        /// <param name="e">Trade update event args</param>
        private void ProcessTradeUpdate(IGTradeUpdateEventArgs e)
        {
            if (e == null) return;

            Log.Trace($"IGBrokerage.ProcessTradeUpdate(): DealId={e.DealId}, Status={e.Status}, " +
                     $"Price={e.FilledPrice}, Size={e.FilledSize}");

            // Find the order by broker ID
            if (!_ordersByBrokerId.TryGetValue(e.DealId, out var order))
            {
                Log.Trace($"IGBrokerage.ProcessTradeUpdate(): Order not found for DealId {e.DealId}");
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
                // Convert IG fill price/size to LEAN format
                var epic = _subscribedEpics.Values.FirstOrDefault(); // fallback
                if (_brokerIdByOrderId.TryGetValue(order.Id, out var ordDealId))
                {
                    var mappedEpic = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                    if (!string.IsNullOrEmpty(mappedEpic)) epic = mappedEpic;
                }
                _instrumentConversion.TryGetValue(epic ?? "", out var conv);
                var fillPipValue = conv.PipValue != 0 ? conv.PipValue : 1m;
                var fillContractSize = conv.ContractSize != 0 ? conv.ContractSize : 1m;

                orderEvent.FillPrice = (e.FilledPrice ?? 0) * fillPipValue;
                orderEvent.FillQuantity = (e.FilledSize ?? 0) * fillContractSize;

                // Calculate order fees for filled orders
                orderEvent.OrderFee = IGOrderFeeCalculator.CalculateFee(
                    order,
                    orderEvent.FillPrice,
                    orderEvent.FillQuantity
                );
            }

            // Remove from tracking if filled or cancelled
            if (status == OrderStatus.Filled || status == OrderStatus.Canceled)
            {
                _ordersByBrokerId.TryRemove(e.DealId, out _);
                _brokerIdByOrderId.TryRemove(order.Id, out _);
            }

            OnOrderEvent(orderEvent);
        }

        /// <summary>
        /// Handles trade/order updates from Lightstreamer
        /// </summary>
        private void HandleTradeUpdate(object sender, IGTradeUpdateEventArgs e)
        {
            try
            {
                // Use message handler to ensure thread-safe processing
                _messageHandler.HandleNewMessage(e);
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
            Log.Error("IGBrokerage.HandleStreamingDisconnect(): Disconnected from Lightstreamer");
            _isConnected = false;
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Disconnect, -1,
                "Lightstreamer streaming connection lost."));
        }

        /// <summary>
        /// Monitors streaming connection and automatically reconnects if disconnected
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to stop monitoring</param>
        private async Task MonitorOrderConnection(CancellationToken cancellationToken)
        {
            var reconnectDelay = TimeSpan.FromSeconds(5);
            const int maxReconnectDelay = 60; // seconds

            Log.Trace("IGBrokerage.MonitorOrderConnection(): Starting connection monitoring...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Wait before checking again
                    await Task.Delay(reconnectDelay, cancellationToken);

                    // Check if streaming client is disconnected while main brokerage thinks it's connected
                    if (_streamingClient != null && !_streamingClient.IsConnected && _isConnected)
                    {
                        Log.Trace("IGBrokerage.MonitorOrderConnection(): Streaming disconnected, attempting reconnection...");

                        try
                        {
                            // Create a new streaming client and connect
                            _streamingClient?.Dispose();
                            _streamingClient = new IGLightstreamerClient(
                                _lightstreamerEndpoint,
                                _cst,
                                _securityToken,
                                _accountId
                            );
                            _streamingClient.OnPriceUpdate += HandlePriceUpdate;
                            _streamingClient.OnTradeUpdate += HandleTradeUpdate;
                            _streamingClient.OnDisconnect += HandleStreamingDisconnect;
                            _streamingClient.Connect();

                            // Resubscribe to trade updates
                            _streamingClient.SubscribeToTradeUpdates();

                            // Resubscribe to all market data
                            var symbolCount = 0;
                            foreach (var kvp in _subscribedEpics)
                            {
                                try
                                {
                                    _streamingClient.SubscribeToPrices(kvp.Value);
                                    symbolCount++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Error($"IGBrokerage.MonitorOrderConnection(): Failed to resubscribe {kvp.Key}: {ex.Message}");
                                }
                            }

                            Log.Trace($"IGBrokerage.MonitorOrderConnection(): Reconnection successful. Resubscribed to {symbolCount} symbols");

                            // Reset delay on success
                            reconnectDelay = TimeSpan.FromSeconds(5);

                            // Notify of reconnection
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Reconnect, -1,
                                $"Reconnected to streaming. Resubscribed to {symbolCount} symbols"));
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"IGBrokerage.MonitorOrderConnection(): Reconnection failed: {ex.Message}");

                            // Exponential backoff
                            reconnectDelay = TimeSpan.FromSeconds(
                                Math.Min(reconnectDelay.TotalSeconds * 2, maxReconnectDelay)
                            );

                            Log.Trace($"IGBrokerage.MonitorOrderConnection(): Next reconnection attempt in {reconnectDelay.TotalSeconds}s");

                            // Notify of failed reconnection
                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1,
                                $"Reconnection failed: {ex.Message}. Retrying in {reconnectDelay.TotalSeconds}s"));
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    // Expected on shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.MonitorOrderConnection(): Monitoring error: {ex.Message}");
                }
            }

            Log.Trace("IGBrokerage.MonitorOrderConnection(): Monitoring stopped");
        }

        /// <summary>
        /// Polls IG REST API for price data as fallback when Lightstreamer streaming fails
        /// </summary>
        private async Task PollPricesViaRest(CancellationToken cancellationToken)
        {
            // Wait a few seconds for Lightstreamer to attempt connection
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

            Log.Trace("IGBrokerage.PollPricesViaRest(): Starting REST price polling fallback...");

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Only poll if we have subscriptions and the streaming client isn't connected
                    if (_subscribedEpics.Count > 0 && _restClient != null)
                    {
                        foreach (var kvp in _subscribedEpics)
                        {
                            if (cancellationToken.IsCancellationRequested) break;

                            try
                            {
                                // Get or fetch conversion info for this EPIC
                                var conversion = GetInstrumentConversion(kvp.Value);

                                _nonTradingRateGate.WaitToProceed();
                                var marketDetails = _restClient.GetMarketDetails(kvp.Value);

                                var snapshot = marketDetails["snapshot"];
                                if (snapshot != null)
                                {
                                    var bid = snapshot["bid"]?.Value<decimal>();
                                    var ask = snapshot["offer"]?.Value<decimal>();

                                    if (bid.HasValue && ask.HasValue)
                                    {
                                        // Convert IG points to standard prices
                                        var bidPrice = ConvertIGPriceToLean(bid.Value, conversion.PipValue);
                                        var askPrice = ConvertIGPriceToLean(ask.Value, conversion.PipValue);

                                        var tick = new Tick
                                        {
                                            Symbol = kvp.Key,
                                            Time = DateTime.UtcNow,
                                            Value = (bidPrice + askPrice) / 2,
                                            BidPrice = bidPrice,
                                            AskPrice = askPrice,
                                            TickType = TickType.Quote
                                        };

                                        if (_aggregator != null)
                                        {
                                            lock (_lock)
                                            {
                                                _aggregator.Update(tick);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error($"IGBrokerage.PollPricesViaRest(): Error polling {kvp.Value}: {ex.Message}");
                            }
                        }
                    }

                    // Poll every 2 seconds
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.PollPricesViaRest(): Error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }

            Log.Trace("IGBrokerage.PollPricesViaRest(): Polling stopped");
        }

        /// <summary>
        /// Polls for deal confirmation via REST API (fallback when Lightstreamer is unavailable)
        /// </summary>
        private void PollDealConfirmation(Order order, string dealReference, decimal pipValue, decimal contractSize)
        {
            try
            {
                // Wait briefly for the deal to be processed
                Thread.Sleep(500);

                _nonTradingRateGate.WaitToProceed();
                var confirmation = _restClient.GetDealConfirmation(dealReference);

                var dealStatus = confirmation["dealStatus"]?.ToString();
                var reason = confirmation["reason"]?.ToString();
                var dealId = confirmation["dealId"]?.ToString();
                var level = confirmation["level"]?.Value<decimal>();
                var size = confirmation["size"]?.Value<decimal>();

                Log.Trace($"IGBrokerage.PollDealConfirmation(): DealRef={dealReference}, Status={dealStatus}, " +
                         $"Reason={reason}, Level={level}, Size={size}, DealId={dealId}");

                if (dealStatus == "ACCEPTED")
                {
                    // Update broker ID mapping to use dealId
                    if (!string.IsNullOrEmpty(dealId))
                    {
                        _ordersByBrokerId.TryRemove(dealReference, out _);
                        _ordersByBrokerId[dealId] = order;
                        _brokerIdByOrderId[order.Id] = dealId;
                    }

                    // Convert fill price and size from IG format
                    var fillPrice = level.HasValue ? level.Value * pipValue : 0m;
                    var fillQuantity = size.HasValue ? size.Value * contractSize : Math.Abs(order.Quantity);
                    if (order.Direction == OrderDirection.Sell) fillQuantity = -fillQuantity;

                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Filled,
                        FillPrice = fillPrice,
                        FillQuantity = fillQuantity
                    });

                    Log.Trace($"IGBrokerage.PollDealConfirmation(): Order {order.Id} filled at {fillPrice}");
                }
                else if (dealStatus == "REJECTED")
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = $"Order rejected: {reason}"
                    });

                    Log.Trace($"IGBrokerage.PollDealConfirmation(): Order {order.Id} rejected: {reason}");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.PollDealConfirmation(): Error: {ex.Message}");
            }
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
                    Log.Trace($"IGBrokerage.MapIGStatusToOrderStatus(): Unknown status '{igStatus}'");
                    return OrderStatus.None;
            }
        }

        /// <summary>
        /// Parses stop loss and take profit prices from order tag
        /// </summary>
        /// <param name="tag">Order tag with format "SL:1.1000;TP:1.2000"</param>
        /// <param name="stopLossPrice">Parsed stop loss price</param>
        /// <param name="takeProfitPrice">Parsed take profit price</param>
        private void ParseStopLossAndTakeProfit(string tag, out decimal? stopLossPrice, out decimal? takeProfitPrice)
        {
            stopLossPrice = null;
            takeProfitPrice = null;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            var parts = tag.Split(';');
            foreach (var part in parts)
            {
                var keyValue = part.Split(':');
                if (keyValue.Length != 2)
                {
                    continue;
                }

                var key = keyValue[0].Trim().ToUpperInvariant();
                var value = keyValue[1].Trim();

                if (key == "SL" && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                {
                    stopLossPrice = sl;
                    Log.Trace($"IGBrokerage.ParseStopLossAndTakeProfit(): Parsed stop loss: {sl}");
                }
                else if (key == "TP" && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                {
                    takeProfitPrice = tp;
                    Log.Trace($"IGBrokerage.ParseStopLossAndTakeProfit(): Parsed take profit: {tp}");
                }
            }
        }

        /// <summary>
        /// Calculates the price distance in points between entry and target price
        /// </summary>
        /// <param name="entryPrice">Entry price for the order</param>
        /// <param name="targetPrice">Target price (stop loss or take profit)</param>
        /// <param name="direction">Order direction</param>
        /// <returns>Distance in points (always positive)</returns>
        private decimal CalculatePriceDistance(decimal entryPrice, decimal targetPrice, OrderDirection direction)
        {
            // For buy orders:
            // - Stop loss is below entry price: distance = entry - stop
            // - Take profit is above entry price: distance = takeProfit - entry
            // For sell orders:
            // - Stop loss is above entry price: distance = stop - entry
            // - Take profit is below entry price: distance = entry - takeProfit

            decimal distance;

            if (direction == OrderDirection.Buy)
            {
                // For buy orders, stop is below, take profit is above
                distance = Math.Abs(entryPrice - targetPrice);
            }
            else
            {
                // For sell orders, stop is above, take profit is below
                distance = Math.Abs(targetPrice - entryPrice);
            }

            return distance;
        }

        #endregion
    }
}
