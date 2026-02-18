/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2026 QuantConnect Corporation.
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
using QuantConnect.Logging;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// IG Markets brokerage implementation for live trading via REST API and Lightstreamer streaming
    /// </summary>
    [BrokerageFactory(typeof(IGBrokerageFactory))]
    public partial class IGBrokerage : Brokerage, IDataQueueHandler, IDataQueueUniverseProvider
    {
        private volatile bool _isConnected;
        private bool _isInitialized;
        private readonly Lock _lock = new();

        private string _apiUrl;
        private string _identifier;
        private string _password;
        private string _apiKey;
        private string _accountId;
        private IAlgorithm _algorithm;
        private IDataAggregator _aggregator;

        private IGRestApiClient _restClient;
        private IGLightstreamerClient _streamingClient;
        internal IGSymbolMapper _symbolMapper;

        private readonly ConcurrentDictionary<string, Order> _ordersByBrokerId = new();
        private readonly ConcurrentDictionary<int, string> _brokerIdByOrderId = new();

        private EventBasedDataQueueHandlerSubscriptionManager _subscriptionManager;
        private readonly ConcurrentDictionary<Symbol, string> _subscribedEpics = new();

        private BrokerageConcurrentMessageHandler<IGTradeUpdateEventArgs> _messageHandler;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _reconnectionMonitorTask;

        // IG instrument conversion cache: EPIC -> (PipValue, ContractSize)
        // IG returns forex prices in "points" (e.g., EURUSD 11792.2 = 1.17922)
        // PipValue converts IG points to standard price: standard = igPrice * pipValue
        // ContractSize converts LEAN quantity to IG contracts: igSize = leanQty / contractSize
        private readonly ConcurrentDictionary<string, (decimal PipValue, decimal ContractSize)> _instrumentConversion = new();

        #region Constructors

        /// <summary>
        /// Parameterless constructor required for <see cref="IDataQueueHandler"/>
        /// </summary>
        public IGBrokerage()
            : this(
                Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal"),
                Config.Get("ig-username"),
                Config.Get("ig-password"),
                Config.Get("ig-api-key"),
                Config.Get("ig-account-id"),
                null)
        {
        }

        /// <summary>
        /// Creates a new instance of the IGBrokerage class
        /// </summary>
        /// <param name="apiUrl">IG API base URL (demo or live)</param>
        /// <param name="identifier">IG username</param>
        /// <param name="password">IG account password</param>
        /// <param name="apiKey">IG API key</param>
        /// <param name="accountId">IG account ID</param>
        /// <param name="algorithm">The algorithm instance</param>
        public IGBrokerage(
            string apiUrl,
            string identifier,
            string password,
            string apiKey,
            string accountId,
            IAlgorithm algorithm)
            : base("IG")
        {
            Initialize(apiUrl, identifier, password, apiKey, accountId, algorithm);
        }

        #endregion

        /// <summary>
        /// Returns true if connected to the broker
        /// </summary>
        public override bool IsConnected => _isConnected;

        #region Initialization

        /// <summary>
        /// Initializes brokerage state. Called from constructors.
        /// </summary>
        protected void Initialize(
            string apiUrl,
            string identifier,
            string password,
            string apiKey,
            string accountId,
            IAlgorithm algorithm)
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;

            _apiUrl = apiUrl;
            _identifier = identifier;
            _password = password;
            _apiKey = apiKey;
            _accountId = accountId;
            _algorithm = algorithm;

            _symbolMapper = new IGSymbolMapper();
            _aggregator = Composer.Instance.GetPart<IDataAggregator>();
            _restClient = new IGRestApiClient(apiUrl, apiKey, accountId);

            _subscriptionManager = new EventBasedDataQueueHandlerSubscriptionManager();
            _subscriptionManager.SubscribeImpl += (s, t) => Subscribe(s);
            _subscriptionManager.UnsubscribeImpl += (s, t) => Unsubscribe(s);

            _messageHandler = new BrokerageConcurrentMessageHandler<IGTradeUpdateEventArgs>(ProcessTradeUpdate);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Connection

        /// <summary>
        /// Connects to IG Markets via REST API and Lightstreamer streaming
        /// </summary>
        public override void Connect()
        {
            if (_isConnected)
            {
                return;
            }

            Log.Trace("IGBrokerage.Connect(): Connecting to IG Markets...");

            // Authenticate via REST
            var loginResponse = _restClient.Login(_identifier, _password);
            Log.Trace($"IGBrokerage.Connect(): Authenticated. Account: {loginResponse.AccountId}");

            // Create streaming client via REST client factory (tokens are private to REST client)
            _streamingClient = _restClient.CreateStreamingClient(_accountId);

            _streamingClient.OnPriceUpdate += HandlePriceUpdate;
            _streamingClient.OnTradeUpdate += HandleTradeUpdate;
            _streamingClient.OnAccountUpdate += HandleAccountUpdate;
            _streamingClient.OnError += HandleStreamingError;
            _streamingClient.OnDisconnect += HandleStreamingDisconnect;

            _streamingClient.Connect();
            _streamingClient.SubscribeToTradeUpdates();
            _streamingClient.SubscribeToAccountUpdates();

            _isConnected = true;

            // Start reconnection monitoring
            _reconnectionMonitorTask = Task.Run(() =>
                MonitorOrderConnection(_cancellationTokenSource.Token));

            Log.Trace("IGBrokerage.Connect(): Connected to IG Markets");
        }

        /// <summary>
        /// Disconnects from IG Markets
        /// </summary>
        public override void Disconnect()
        {
            if (!_isConnected)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();

            try
            {
                _reconnectionMonitorTask?.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Expected on shutdown
            }

            if (_streamingClient != null)
            {
                _streamingClient.Disconnect();
                _streamingClient.Dispose();
                _streamingClient = null;
            }

            _restClient?.Logout();

            _isConnected = false;
            Log.Trace("IGBrokerage.Disconnect(): Disconnected from IG Markets");
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public override void Dispose()
        {
            Disconnect();
            _cancellationTokenSource?.Dispose();
            _messageHandler?.Dispose();
            _restClient?.Dispose();
            base.Dispose();
        }

        #endregion

        #region IBrokerage Order Methods

        /// <summary>
        /// Gets all open orders on the account
        /// </summary>
        public override List<Order> GetOpenOrders()
        {
            var workingOrders = _restClient.GetWorkingOrders();
            var orders = new List<Order>();

            foreach (var wo in workingOrders)
            {
                var symbol = _symbolMapper.GetLeanSymbol(wo.Epic, SecurityType.Forex, IGSymbolMapper.MarketName);
                if (symbol == null)
                {
                    Log.Trace($"IGBrokerage.GetOpenOrders(): Unable to map EPIC {wo.Epic}");
                    continue;
                }

                var conversion = GetInstrumentConversion(wo.Epic);
                var direction = wo.Direction == "BUY" ? OrderDirection.Buy : OrderDirection.Sell;
                var quantity = direction == OrderDirection.Buy
                    ? wo.Size * conversion.ContractSize
                    : -wo.Size * conversion.ContractSize;
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
                    Log.Trace($"IGBrokerage.GetOpenOrders(): Unsupported order type {wo.OrderType}");
                    continue;
                }

                _ordersByBrokerId[wo.DealId] = order;
                _brokerIdByOrderId[order.Id] = wo.DealId;
                orders.Add(order);
            }

            return orders;
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        public override List<Holding> GetAccountHoldings()
        {
            var positions = _restClient.GetPositions();
            var holdings = new List<Holding>();

            foreach (var position in positions)
            {
                var symbol = _symbolMapper.GetLeanSymbol(position.Epic, SecurityType.Forex, IGSymbolMapper.MarketName);
                if (symbol == null)
                {
                    Log.Trace($"IGBrokerage.GetAccountHoldings(): Unable to map EPIC {position.Epic}");
                    continue;
                }

                var conversion = GetInstrumentConversion(position.Epic);
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
            }

            return holdings;
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the account
        /// </summary>
        public override List<CashAmount> GetCashBalance()
        {
            var account = _restClient.GetAccountBalance();
            return new List<CashAmount>
            {
                new CashAmount(account.Balance.Available, account.Currency ?? "GBP")
            };
        }

        /// <summary>
        /// Places a new order
        /// </summary>
        public override bool PlaceOrder(Order order)
        {
            Log.Trace($"IGBrokerage.PlaceOrder(): {order.Id} for {order.Symbol}");

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
                Log.Error($"IGBrokerage.PlaceOrder(): Error: {ex.Message}");
                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = OrderStatus.Invalid,
                    Message = ex.Message
                });
                return false;
            }
        }

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

            var conversion = GetInstrumentConversion(epic);
            var igSize = Math.Abs(order.Quantity) / conversion.ContractSize;

            var request = new IGPlaceOrderRequest
            {
                Epic = epic,
                Direction = order.Quantity > 0 ? "BUY" : "SELL",
                Size = igSize,
                CurrencyCode = "USD",
                Expiry = "-",
                ForceOpen = true,
                GuaranteedStop = false
            };

            // Set order type specific parameters
            decimal? entryPrice = null;
            switch (order.Type)
            {
                case OrderType.Market:
                case OrderType.MarketOnOpen:
                    request.OrderType = "MARKET";
                    break;

                case OrderType.Limit:
                    var limitOrder = (LimitOrder)order;
                    request.OrderType = "LIMIT";
                    request.Level = ConvertLeanPriceToIG(limitOrder.LimitPrice, conversion.PipValue);
                    entryPrice = limitOrder.LimitPrice;
                    break;

                case OrderType.StopMarket:
                    var stopOrder = (StopMarketOrder)order;
                    request.OrderType = "STOP";
                    request.Level = ConvertLeanPriceToIG(stopOrder.StopPrice, conversion.PipValue);
                    entryPrice = stopOrder.StopPrice;
                    break;

                case OrderType.StopLimit:
                    var stopLimitOrder = (StopLimitOrder)order;
                    request.OrderType = "LIMIT";
                    request.Level = ConvertLeanPriceToIG(stopLimitOrder.LimitPrice, conversion.PipValue);
                    entryPrice = stopLimitOrder.LimitPrice;
                    break;

                default:
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = $"Unsupported order type: {order.Type}"
                    });
                    return false;
            }

            // Parse stop loss and take profit from order tag
            if (!string.IsNullOrEmpty(order.Tag))
            {
                ParseStopLossAndTakeProfit(order.Tag, out var stopLossPrice, out var takeProfitPrice);
                SetStopAndLimitDistances(request, order, entryPrice, stopLossPrice, takeProfitPrice, conversion.PipValue);
            }

            try
            {
                var dealReference = _restClient.PlaceOrder(request);

                _brokerIdByOrderId[order.Id] = dealReference;
                _ordersByBrokerId[dealReference] = order;

                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = OrderStatus.Submitted
                });

                Log.Trace($"IGBrokerage.PlaceOrderInternal(): Order {order.Id} submitted. DealRef: {dealReference}");

                PollDealConfirmation(order, dealReference, conversion.PipValue, conversion.ContractSize);
                return true;
            }
            catch (Exception ex)
            {
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
        public override bool UpdateOrder(Order order)
        {
            Log.Trace($"IGBrokerage.UpdateOrder(): {order.Id}");

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
                Log.Error($"IGBrokerage.UpdateOrder(): Error: {ex.Message}");
                return false;
            }
        }

        private bool UpdateOrderInternal(Order order)
        {
            if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
            {
                Log.Error($"IGBrokerage.UpdateOrderInternal(): No broker ID found for order {order.Id}");
                return false;
            }

            var request = new IGUpdateOrderRequest { DealId = dealId };

            switch (order.Type)
            {
                case OrderType.Limit:
                    request.Level = ((LimitOrder)order).LimitPrice;
                    break;
                case OrderType.StopMarket:
                    request.Level = ((StopMarketOrder)order).StopPrice;
                    break;
                case OrderType.StopLimit:
                    request.Level = ((StopLimitOrder)order).LimitPrice;
                    break;
                default:
                    Log.Error($"IGBrokerage.UpdateOrderInternal(): Cannot update order type {order.Type}");
                    return false;
            }

            try
            {
                var dealReference = _restClient.UpdateOrder(request);

                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = OrderStatus.UpdateSubmitted
                });

                Log.Trace($"IGBrokerage.UpdateOrderInternal(): Order {order.Id} updated. DealRef: {dealReference}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.UpdateOrderInternal(): Failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        public override bool CancelOrder(Order order)
        {
            Log.Trace($"IGBrokerage.CancelOrder(): {order.Id}");

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
                Log.Error($"IGBrokerage.CancelOrder(): Error: {ex.Message}");
                return false;
            }
        }

        private bool CancelOrderInternal(Order order)
        {
            if (!_brokerIdByOrderId.TryGetValue(order.Id, out var dealId))
            {
                Log.Error($"IGBrokerage.CancelOrderInternal(): No broker ID found for order {order.Id}");
                return false;
            }

            try
            {
                _restClient.CancelOrder(dealId);

                OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                {
                    Status = OrderStatus.Canceled
                });

                _brokerIdByOrderId.TryRemove(order.Id, out _);
                _ordersByBrokerId.TryRemove(dealId, out _);

                Log.Trace($"IGBrokerage.CancelOrderInternal(): Order {order.Id} canceled");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.CancelOrderInternal(): Failed: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region IDataQueueHandler

        /// <summary>
        /// Subscribe to the specified configuration
        /// </summary>
        public IEnumerator<BaseData> Subscribe(SubscriptionDataConfig dataConfig, EventHandler newDataAvailableHandler)
        {
            if (!CanSubscribe(dataConfig.Symbol))
            {
                return null;
            }

            if (_aggregator == null)
            {
                _aggregator = Composer.Instance.GetPart<IDataAggregator>();
            }

            var enumerator = _aggregator.Add(dataConfig, newDataAvailableHandler);
            _subscriptionManager.Subscribe(dataConfig);

            return enumerator;
        }

        /// <summary>
        /// Removes the specified configuration
        /// </summary>
        public void Unsubscribe(SubscriptionDataConfig dataConfig)
        {
            _subscriptionManager.Unsubscribe(dataConfig);
            _aggregator?.Remove(dataConfig);
        }

        /// <summary>
        /// Sets the job we're subscribing for
        /// </summary>
        public void SetJob(LiveNodePacket job)
        {
            if (_aggregator == null)
            {
                _aggregator = Composer.Instance.GetPart<IDataAggregator>();
            }
        }

        #endregion

        #region IDataQueueUniverseProvider

        /// <summary>
        /// Returns symbols available at the data source matching the given symbol
        /// </summary>
        public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
        {
            if (!_isConnected || _restClient == null)
            {
                return Enumerable.Empty<Symbol>();
            }

            var markets = _restClient.SearchMarkets(symbol.Value);
            var symbols = new List<Symbol>();

            foreach (var market in markets)
            {
                var securityType = MapInstrumentType(market.InstrumentType);
                var leanSymbol = _symbolMapper.GetLeanSymbol(market.Epic, securityType, IGSymbolMapper.MarketName);

                if (leanSymbol != null)
                {
                    symbols.Add(leanSymbol);
                }
                else
                {
                    var ticker = market.InstrumentName?.Replace(" ", "") ?? market.Epic;
                    symbols.Add(Symbol.Create(ticker, securityType, IGSymbolMapper.MarketName));
                }
            }

            return symbols;
        }

        /// <summary>
        /// Returns whether selection can take place
        /// </summary>
        public bool CanPerformSelection()
        {
            return _isConnected;
        }

        #endregion

        #region Internal Testing Methods

        /// <summary>
        /// Gets the current ask price for a symbol (for testing)
        /// </summary>
        internal decimal GetCurrentAskPrice(Symbol symbol)
        {
            var epic = _symbolMapper.GetBrokerageSymbol(symbol);
            var marketDetails = _restClient.GetMarketData(epic);
            var conversion = GetInstrumentConversion(epic);
            return (marketDetails.Snapshot?.Offer ?? 0) * conversion.PipValue;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Determines if we can subscribe to the specified symbol
        /// </summary>
        private bool CanSubscribe(Symbol symbol)
        {
            if (symbol.Value.IndexOfInvariant("universe", true) != -1 || symbol.IsCanonical())
            {
                return false;
            }

            return IGSymbolMapper.SupportedSecurityTypes.Contains(symbol.SecurityType);
        }

        /// <summary>
        /// Subscribes to price updates for the specified symbols
        /// </summary>
        private bool Subscribe(IEnumerable<Symbol> symbols)
        {
            if (_streamingClient == null || !_streamingClient.IsConnected)
            {
                throw new InvalidOperationException(
                    "IGBrokerage.Subscribe(): Cannot subscribe - streaming connection is not available. " +
                    "Ensure Connect() has been called and streaming is active.");
            }

            foreach (var symbol in symbols)
            {
                var epic = _symbolMapper.GetBrokerageSymbol(symbol);
                if (!string.IsNullOrEmpty(epic))
                {
                    _subscribedEpics[symbol] = epic;
                    GetInstrumentConversion(epic);
                    _streamingClient.SubscribeToPrices(epic);
                    Log.Trace($"IGBrokerage.Subscribe(): Subscribed to {symbol} (EPIC: {epic})");
                }
            }
            return true;
        }

        /// <summary>
        /// Unsubscribes from price updates for the specified symbols
        /// </summary>
        private bool Unsubscribe(IEnumerable<Symbol> symbols)
        {
            foreach (var symbol in symbols)
            {
                if (_subscribedEpics.TryRemove(symbol, out var epic))
                {
                    _streamingClient?.UnsubscribeFromPrices(epic);
                    Log.Trace($"IGBrokerage.Unsubscribe(): Unsubscribed from {symbol} (EPIC: {epic})");
                }
            }
            return true;
        }

        /// <summary>
        /// Fetches and caches instrument conversion info (pip value and contract size) for an EPIC
        /// </summary>
        internal (decimal PipValue, decimal ContractSize) GetInstrumentConversion(string epic)
        {
            if (_instrumentConversion.TryGetValue(epic, out var cached))
            {
                return cached;
            }

            try
            {
                var marketDetails = _restClient.GetMarketData(epic);
                var instrument = marketDetails.Instrument;

                decimal pipValue = 1m;
                if (!string.IsNullOrEmpty(instrument?.OnePipMeans))
                {
                    var parts = instrument.OnePipMeans.Split(' ');
                    if (parts.Length > 0 && decimal.TryParse(parts[0], NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var pv))
                    {
                        pipValue = pv;
                    }
                }

                decimal contractSize = 1m;
                if (!string.IsNullOrEmpty(instrument?.ContractSize) &&
                    decimal.TryParse(instrument.ContractSize, NumberStyles.Any,
                        CultureInfo.InvariantCulture, out var cs))
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
                return (1m, 1m);
            }
        }

        private static decimal ConvertIGPriceToLean(decimal igPrice, decimal pipValue)
        {
            return igPrice * pipValue;
        }

        private static decimal ConvertLeanPriceToIG(decimal leanPrice, decimal pipValue)
        {
            if (pipValue == 0) return leanPrice;
            return leanPrice / pipValue;
        }

        /// <summary>
        /// Maps IG instrument type string to LEAN SecurityType
        /// </summary>
        private static SecurityType MapInstrumentType(string instrumentType)
        {
            return instrumentType?.ToUpperInvariant() switch
            {
                "CURRENCIES" => SecurityType.Forex,
                "INDICES" => SecurityType.Index,
                "COMMODITIES" => SecurityType.Cfd,
                "SHARES" => SecurityType.Equity,
                "CRYPTOCURRENCIES" => SecurityType.Crypto,
                _ => SecurityType.Cfd
            };
        }

        #endregion

        #region Streaming Event Handlers

        private void HandlePriceUpdate(object sender, IGPriceUpdateEventArgs e)
        {
            var symbol = _subscribedEpics.FirstOrDefault(kvp => kvp.Value == e.Epic).Key;
            if (symbol == null || !e.Bid.HasValue || !e.Ask.HasValue)
            {
                return;
            }

            _instrumentConversion.TryGetValue(e.Epic, out var conversion);
            var pipValue = conversion.PipValue != 0 ? conversion.PipValue : 1m;

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
                _aggregator?.Update(tick);
            }
        }

        private void ProcessTradeUpdate(IGTradeUpdateEventArgs e)
        {
            if (e == null) return;

            Log.Trace($"IGBrokerage.ProcessTradeUpdate(): DealId={e.DealId}, Status={e.Status}");

            if (!_ordersByBrokerId.TryGetValue(e.DealId, out var order))
            {
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
                var epic = _symbolMapper.GetBrokerageSymbol(order.Symbol);
                _instrumentConversion.TryGetValue(epic ?? "", out var conv);
                var fillPipValue = conv.PipValue != 0 ? conv.PipValue : 1m;
                var fillContractSize = conv.ContractSize != 0 ? conv.ContractSize : 1m;

                orderEvent.FillPrice = (e.FilledPrice ?? 0) * fillPipValue;
                orderEvent.FillQuantity = (e.FilledSize ?? 0) * fillContractSize;
            }

            if (status == OrderStatus.Filled || status == OrderStatus.Canceled)
            {
                _ordersByBrokerId.TryRemove(e.DealId, out _);
                _brokerIdByOrderId.TryRemove(order.Id, out _);
            }

            OnOrderEvent(orderEvent);
        }

        private void HandleTradeUpdate(object sender, IGTradeUpdateEventArgs e)
        {
            _messageHandler.HandleNewMessage(e);
        }

        private void HandleAccountUpdate(object sender, IGAccountUpdateEventArgs e)
        {
            Log.Trace($"IGBrokerage.HandleAccountUpdate(): Balance={e.Balance} {e.Currency}, Available={e.AvailableCash}");
            OnAccountChanged(new AccountEvent(e.Currency, e.AvailableCash));
        }

        private void HandleStreamingError(object sender, IGStreamingErrorEventArgs e)
        {
            Log.Error($"IGBrokerage.HandleStreamingError(): Code={e.Code}, Message={e.Message}");
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, e.Code, e.Message));
        }

        private void HandleStreamingDisconnect(object sender, EventArgs e)
        {
            Log.Error("IGBrokerage.HandleStreamingDisconnect(): Disconnected from Lightstreamer");
            _isConnected = false;
            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Disconnect, -1,
                "Lightstreamer streaming connection lost."));
        }

        #endregion

        #region Connection Monitoring

        /// <summary>
        /// Monitors streaming connection and automatically reconnects if disconnected
        /// </summary>
        private async Task MonitorOrderConnection(CancellationToken cancellationToken)
        {
            var reconnectDelay = TimeSpan.FromSeconds(5);
            const int maxReconnectDelay = 60;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(reconnectDelay, cancellationToken);

                    if (_streamingClient != null && !_streamingClient.IsConnected && _isConnected)
                    {
                        Log.Trace("IGBrokerage.MonitorOrderConnection(): Streaming disconnected, reconnecting...");

                        try
                        {
                            _streamingClient?.Dispose();
                            _streamingClient = _restClient.CreateStreamingClient(_accountId);

                            _streamingClient.OnPriceUpdate += HandlePriceUpdate;
                            _streamingClient.OnTradeUpdate += HandleTradeUpdate;
                            _streamingClient.OnDisconnect += HandleStreamingDisconnect;

                            _streamingClient.Connect();
                            _streamingClient.SubscribeToTradeUpdates();

                            var symbolCount = 0;
                            foreach (var kvp in _subscribedEpics)
                            {
                                _streamingClient.SubscribeToPrices(kvp.Value);
                                symbolCount++;
                            }

                            Log.Trace($"IGBrokerage.MonitorOrderConnection(): Reconnected. Resubscribed to {symbolCount} symbols");
                            reconnectDelay = TimeSpan.FromSeconds(5);

                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Reconnect, -1,
                                $"Reconnected to streaming. Resubscribed to {symbolCount} symbols"));
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"IGBrokerage.MonitorOrderConnection(): Reconnection failed: {ex.Message}");
                            reconnectDelay = TimeSpan.FromSeconds(
                                Math.Min(reconnectDelay.TotalSeconds * 2, maxReconnectDelay));

                            OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, -1,
                                $"Reconnection failed: {ex.Message}. Retrying in {reconnectDelay.TotalSeconds}s"));
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.MonitorOrderConnection(): Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Polls for deal confirmation via REST API
        /// </summary>
        private void PollDealConfirmation(Order order, string dealReference, decimal pipValue, decimal contractSize)
        {
            Thread.Sleep(500);

            try
            {
                var confirmation = _restClient.GetDealConfirmation(dealReference);

                Log.Trace($"IGBrokerage.PollDealConfirmation(): DealRef={dealReference}, " +
                         $"Status={confirmation.DealStatus}, DealId={confirmation.DealId}");

                if (confirmation.DealStatus == "ACCEPTED")
                {
                    if (!string.IsNullOrEmpty(confirmation.DealId))
                    {
                        _ordersByBrokerId.TryRemove(dealReference, out _);
                        _ordersByBrokerId[confirmation.DealId] = order;
                        _brokerIdByOrderId[order.Id] = confirmation.DealId;
                    }

                    var fillPrice = confirmation.Level * pipValue;
                    var fillQuantity = confirmation.Size * contractSize;
                    if (order.Direction == OrderDirection.Sell) fillQuantity = -fillQuantity;

                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Filled,
                        FillPrice = fillPrice,
                        FillQuantity = fillQuantity
                    });
                }
                else if (confirmation.DealStatus == "REJECTED")
                {
                    OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
                    {
                        Status = OrderStatus.Invalid,
                        Message = $"Order rejected: {confirmation.Reason}"
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.PollDealConfirmation(): Error: {ex.Message}");
            }
        }

        private static OrderStatus MapIGStatusToOrderStatus(string igStatus)
        {
            return igStatus?.ToUpperInvariant() switch
            {
                "ACCEPTED" or "OPEN" => OrderStatus.Submitted,
                "AMENDED" => OrderStatus.UpdateSubmitted,
                "DELETED" => OrderStatus.Canceled,
                "REJECTED" => OrderStatus.Invalid,
                "FILLED" => OrderStatus.Filled,
                "PARTIALLY_FILLED" => OrderStatus.PartiallyFilled,
                _ => OrderStatus.None
            };
        }

        private static void ParseStopLossAndTakeProfit(string tag, out decimal? stopLossPrice, out decimal? takeProfitPrice)
        {
            stopLossPrice = null;
            takeProfitPrice = null;

            foreach (var part in tag.Split(';'))
            {
                var keyValue = part.Split(':');
                if (keyValue.Length != 2) continue;

                var key = keyValue[0].Trim().ToUpperInvariant();
                var value = keyValue[1].Trim();

                if (key == "SL" && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var sl))
                {
                    stopLossPrice = sl;
                }
                else if (key == "TP" && decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var tp))
                {
                    takeProfitPrice = tp;
                }
            }
        }

        /// <summary>
        /// Sets stop and limit distance on the order request based on parsed SL/TP prices
        /// </summary>
        private void SetStopAndLimitDistances(IGPlaceOrderRequest request, Order order,
            decimal? entryPrice, decimal? stopLossPrice, decimal? takeProfitPrice, decimal pipValue)
        {
            if (!stopLossPrice.HasValue && !takeProfitPrice.HasValue)
            {
                return;
            }

            // For market orders, get current price for distance calculation
            if (entryPrice == null)
            {
                var security = _algorithm?.Securities[order.Symbol];
                if (security != null)
                {
                    entryPrice = order.Direction == OrderDirection.Buy ? security.AskPrice : security.BidPrice;
                }
            }

            if (entryPrice == null)
            {
                return;
            }

            if (stopLossPrice.HasValue)
            {
                var distance = Math.Abs(entryPrice.Value - stopLossPrice.Value);
                if (distance > 0) request.StopDistance = distance;
            }

            if (takeProfitPrice.HasValue)
            {
                var distance = Math.Abs(entryPrice.Value - takeProfitPrice.Value);
                if (distance > 0) request.LimitDistance = distance;
            }
        }

        #endregion
    }
}
