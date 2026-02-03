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
                    // TODO: Initialize REST client and authenticate
                    // TODO: Initialize Lightstreamer client for streaming

                    _isConnected = true;
                    Log.Trace("IGBrokerage.Connect(): Successfully connected to IG Markets");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.Connect(): Failed to connect: {ex.Message}");
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
                    // TODO: Disconnect streaming client
                    // TODO: Logout from REST API

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

            // TODO: Implement REST API call to get working orders
            return new List<Order>();
        }

        /// <summary>
        /// Gets all holdings for the account
        /// </summary>
        /// <returns>The current holdings from the account</returns>
        public override List<Holding> GetAccountHoldings()
        {
            Log.Trace("IGBrokerage.GetAccountHoldings(): Fetching account holdings...");

            // TODO: Implement REST API call to get positions
            return new List<Holding>();
        }

        /// <summary>
        /// Gets the current cash balance for each currency held in the brokerage account
        /// </summary>
        /// <returns>The current cash balance for each currency available for trading</returns>
        public override List<CashAmount> GetCashBalance()
        {
            Log.Trace("IGBrokerage.GetCashBalance(): Fetching cash balance...");

            // TODO: Implement REST API call to get account balance
            return new List<CashAmount>();
        }

        /// <summary>
        /// Places a new order and assigns a new broker ID to the order
        /// </summary>
        /// <param name="order">The order to be placed</param>
        /// <returns>True if the request for a new order has been placed, false otherwise</returns>
        public override bool PlaceOrder(Order order)
        {
            Log.Trace($"IGBrokerage.PlaceOrder(): Placing order {order.Id} for {order.Symbol}");

            // TODO: Implement REST API call to place order
            return false;
        }

        /// <summary>
        /// Updates the order with the same id
        /// </summary>
        /// <param name="order">The new order information</param>
        /// <returns>True if the request was made for the order to be updated, false otherwise</returns>
        public override bool UpdateOrder(Order order)
        {
            Log.Trace($"IGBrokerage.UpdateOrder(): Updating order {order.Id}");

            // TODO: Implement REST API call to update order
            return false;
        }

        /// <summary>
        /// Cancels the order with the specified ID
        /// </summary>
        /// <param name="order">The order to cancel</param>
        /// <returns>True if the request was made for the order to be canceled, false otherwise</returns>
        public override bool CancelOrder(Order order)
        {
            Log.Trace($"IGBrokerage.CancelOrder(): Canceling order {order.Id}");

            // TODO: Implement REST API call to cancel order
            return false;
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
            // TODO: Implement market search via REST API
            return Enumerable.Empty<Symbol>();
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

        #region History Provider

        /// <summary>
        /// Gets the history for the requested symbols
        /// </summary>
        /// <param name="request">The historical data request</param>
        /// <returns>An enumerable of bars covering the span specified in the request</returns>
        public override IEnumerable<BaseData> GetHistory(Data.HistoryRequest request)
        {
            if (!CanSubscribe(request.Symbol))
            {
                return null;
            }

            Log.Trace($"IGBrokerage.GetHistory(): Fetching history for {request.Symbol} " +
                      $"from {request.StartTimeUtc} to {request.EndTimeUtc}");

            // TODO: Implement REST API call to get historical prices
            return Enumerable.Empty<BaseData>();
        }

        #endregion

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
                    // TODO: Subscribe to Lightstreamer price updates for this EPIC
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
                    // TODO: Unsubscribe from Lightstreamer price updates for this EPIC
                    Log.Trace($"IGBrokerage.Unsubscribe(): Unsubscribed from {symbol} (EPIC: {epic})");
                }
            }
            return true;
        }

        #endregion
    }
}
