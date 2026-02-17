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

using com.lightstreamer.client;
using Newtonsoft.Json.Linq;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.IG.Api
{
    /// <summary>
    /// Lightstreamer client for IG Markets streaming data
    /// </summary>
    public class IGLightstreamerClient : IDisposable
    {
        private readonly string _endpoint;
        private readonly string _cst;
        private readonly string _securityToken;
        private readonly string _accountId;

        private LightstreamerClient _client;
        private readonly object _lock = new object();
        private volatile bool _isConnected;

        /// <summary>
        /// Whether the streaming client is currently connected
        /// </summary>
        public bool IsConnected => _isConnected;

        // Event handlers
        public event EventHandler<IGPriceUpdateEventArgs> OnPriceUpdate;
        public event EventHandler<IGTradeUpdateEventArgs> OnTradeUpdate;
        public event EventHandler<IGAccountUpdateEventArgs> OnAccountUpdate;
        public event EventHandler<IGStreamingErrorEventArgs> OnError;
        public event EventHandler OnDisconnect;

        // Subscription tracking
        private readonly Dictionary<string, Subscription> _priceSubscriptions;
        private Subscription _tradeSubscription;
        private Subscription _accountSubscription;

        public IGLightstreamerClient(string endpoint, string cst, string securityToken, string accountId)
        {
            _endpoint = endpoint;
            _cst = cst;
            _securityToken = securityToken;
            _accountId = accountId;

            _priceSubscriptions = new Dictionary<string, Subscription>();
        }

        /// <summary>
        /// Connects to Lightstreamer
        /// </summary>
        public void Connect()
        {
            lock (_lock)
            {
                if (_isConnected)
                    return;

                try
                {
                    Log.Trace($"IGLightstreamerClient.Connect(): Connecting to {_endpoint}");

                    _client = new LightstreamerClient(_endpoint, "DEFAULT");
                    _client.connectionDetails.User = _accountId;
                    _client.connectionDetails.Password = $"CST-{_cst}|XST-{_securityToken}";

                    // Force HTTP polling transport for maximum compatibility
                    _client.connectionOptions.ForcedTransport = "HTTP-POLLING";
                    _client.connectionOptions.PollingInterval = 2000;

                    Log.Trace($"IGLightstreamerClient.Connect(): User={_accountId}, Endpoint={_endpoint}");

                    _client.addListener(new IGConnectionListener(this));
                    _client.connect();
                    _isConnected = true;

                    Log.Trace("IGLightstreamerClient.Connect(): Connected successfully");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGLightstreamerClient.Connect(): Failed: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Disconnects from Lightstreamer
        /// </summary>
        public void Disconnect()
        {
            lock (_lock)
            {
                if (!_isConnected)
                    return;

                try
                {
                    // Unsubscribe from all
                    foreach (var sub in _priceSubscriptions.Values)
                    {
                        try { _client.unsubscribe(sub); } catch { }
                    }
                    _priceSubscriptions.Clear();

                    if (_tradeSubscription != null)
                    {
                        try { _client.unsubscribe(_tradeSubscription); } catch { }
                        _tradeSubscription = null;
                    }

                    if (_accountSubscription != null)
                    {
                        try { _client.unsubscribe(_accountSubscription); } catch { }
                        _accountSubscription = null;
                    }

                    _client.disconnect();
                    _isConnected = false;

                    Log.Trace("IGLightstreamerClient.Disconnect(): Disconnected");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGLightstreamerClient.Disconnect(): Error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Subscribes to price updates for an EPIC
        /// </summary>
        public void SubscribeToPrices(string epic)
        {
            if (_priceSubscriptions.ContainsKey(epic))
                return;

            try
            {
                var subscription = new Subscription(
                    "MERGE",
                    new[] { $"MARKET:{epic}" },
                    new[] { "BID", "OFFER", "HIGH", "LOW", "MID_OPEN", "CHANGE", "CHANGE_PCT",
                            "UPDATE_TIME", "MARKET_STATE", "MARKET_DELAY" }
                );
                subscription.RequestedSnapshot = "yes";

                var listener = new IGPriceListener(epic, this);
                subscription.addListener(listener);
                _client.subscribe(subscription);
                _priceSubscriptions[epic] = subscription;

                Log.Trace($"IGLightstreamerClient.SubscribeToPrices(): Subscribed to {epic}");
            }
            catch (Exception ex)
            {
                Log.Error($"IGLightstreamerClient.SubscribeToPrices(): Failed for {epic}: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribes from price updates for an EPIC
        /// </summary>
        public void UnsubscribeFromPrices(string epic)
        {
            if (_priceSubscriptions.TryGetValue(epic, out var subscription))
            {
                try
                {
                    _client.unsubscribe(subscription);
                    _priceSubscriptions.Remove(epic);
                    Log.Trace($"IGLightstreamerClient.UnsubscribeFromPrices(): Unsubscribed from {epic}");
                }
                catch (Exception ex)
                {
                    Log.Error($"IGLightstreamerClient.UnsubscribeFromPrices(): Failed for {epic}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Subscribes to trade/order updates
        /// </summary>
        public void SubscribeToTradeUpdates()
        {
            if (_tradeSubscription != null)
                return;

            try
            {
                var subscription = new Subscription(
                    "DISTINCT",
                    new[] { $"TRADE:{_accountId}" },
                    new[] { "CONFIRMS", "OPU", "WOU" }
                );
                subscription.RequestedSnapshot = "yes";

                var listener = new IGTradeListener(this);
                subscription.addListener(listener);
                _client.subscribe(subscription);
                _tradeSubscription = subscription;

                Log.Trace("IGLightstreamerClient.SubscribeToTradeUpdates(): Subscribed");
            }
            catch (Exception ex)
            {
                Log.Error($"IGLightstreamerClient.SubscribeToTradeUpdates(): Failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Subscribes to account updates
        /// </summary>
        public void SubscribeToAccountUpdates()
        {
            if (_accountSubscription != null)
                return;

            try
            {
                var subscription = new Subscription(
                    "MERGE",
                    new[] { $"ACCOUNT:{_accountId}" },
                    new[] { "PNL", "DEPOSIT", "AVAILABLE_CASH", "FUNDS", "MARGIN", "MARGIN_LR",
                            "MARGIN_NLR", "AVAILABLE_TO_DEAL", "EQUITY" }
                );
                subscription.RequestedSnapshot = "yes";

                var listener = new IGAccountListener(this);
                subscription.addListener(listener);
                _client.subscribe(subscription);
                _accountSubscription = subscription;

                Log.Trace("IGLightstreamerClient.SubscribeToAccountUpdates(): Subscribed");
            }
            catch (Exception ex)
            {
                Log.Error($"IGLightstreamerClient.SubscribeToAccountUpdates(): Failed: {ex.Message}");
            }
        }

        #region Internal Event Raisers

        internal void RaisePriceUpdate(string epic, decimal? bid, decimal? ask,
            decimal? bidSize, decimal? askSize, decimal? lastPrice, long? volume)
        {
            OnPriceUpdate?.Invoke(this, new IGPriceUpdateEventArgs
            {
                Epic = epic,
                Bid = bid,
                Ask = ask,
                BidSize = bidSize,
                AskSize = askSize,
                LastTradedPrice = lastPrice,
                LastTradedVolume = volume
            });
        }

        internal void RaiseTradeUpdate(string dealId, string status, string reason,
            decimal? filledPrice, decimal? filledSize)
        {
            OnTradeUpdate?.Invoke(this, new IGTradeUpdateEventArgs
            {
                DealId = dealId,
                Status = status,
                Reason = reason,
                FilledPrice = filledPrice,
                FilledSize = filledSize
            });
        }

        internal void RaiseAccountUpdate(string currency, decimal balance, decimal margin,
            decimal available, decimal pnl)
        {
            OnAccountUpdate?.Invoke(this, new IGAccountUpdateEventArgs
            {
                Currency = currency,
                Balance = balance,
                Margin = margin,
                AvailableCash = available,
                PnL = pnl
            });
        }

        internal void RaiseError(int code, string message)
        {
            OnError?.Invoke(this, new IGStreamingErrorEventArgs
            {
                Code = code,
                Message = message
            });
        }

        internal void RaiseDisconnect()
        {
            _isConnected = false;
            OnDisconnect?.Invoke(this, EventArgs.Empty);
        }

        internal void SetConnected(bool connected)
        {
            _isConnected = connected;
        }

        #endregion

        public void Dispose()
        {
            Disconnect();
        }
    }

    #region Lightstreamer Listeners

    internal class IGConnectionListener : ClientListener
    {
        private readonly IGLightstreamerClient _client;

        public IGConnectionListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void onListenEnd() { }

        public void onListenStart() { }

        public void onServerError(int errorCode, string errorMessage)
        {
            Log.Error($"IGLightstreamer: Server error {errorCode}: {errorMessage}");
            _client.RaiseError(errorCode, errorMessage);
        }

        public void onStatusChange(string status)
        {
            Log.Trace($"IGLightstreamer: Status changed to {status}");

            if (status.StartsWith("CONNECTED:"))
            {
                Log.Trace($"IGLightstreamer: Successfully connected via {status}");
                _client.SetConnected(true);
            }
            else if (status == "DISCONNECTED")
            {
                _client.RaiseDisconnect();
            }
            else if (status.Contains("WILL-RETRY"))
            {
                Log.Trace("IGLightstreamer: Connection attempt failed, will retry...");
            }
        }

        public void onPropertyChange(string property)
        {
            Log.Trace($"IGLightstreamer: Property changed: {property}");
        }
    }

    internal class IGPriceListener : SubscriptionListener
    {
        private readonly string _epic;
        private readonly IGLightstreamerClient _client;

        public IGPriceListener(string epic, IGLightstreamerClient client)
        {
            _epic = epic;
            _client = client;
        }

        public void onItemUpdate(ItemUpdate update)
        {
            try
            {
                var bid = ParseDecimal(update.getValue("BID"));
                var ask = ParseDecimal(update.getValue("OFFER"));

                _client.RaisePriceUpdate(_epic, bid, ask, null, null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error($"IGPriceListener.onItemUpdate: Error: {ex.Message}");
            }
        }

        public void onItemLostUpdates(string itemName, int itemPos, int lostUpdates) =>
            Log.Trace($"IGPriceListener: Lost {lostUpdates} updates for {itemName}");

        public void onSubscription() { }
        public void onUnsubscription() { }
        public void onClearSnapshot(string itemName, int itemPos) { }
        public void onCommandSecondLevelItemLostUpdates(int lostUpdates, string key) { }
        public void onCommandSecondLevelSubscriptionError(int code, string message, string key) { }
        public void onEndOfSnapshot(string itemName, int itemPos) { }
        public void onListenEnd() { }
        public void onListenStart() { }
        public void onSubscriptionError(int code, string message) =>
            Log.Error($"IGPriceListener: Subscription error {code}: {message}");
        public void onRealMaxFrequency(string frequency) { }

        private decimal? ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null")
                return null;
            return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : (decimal?)null;
        }
    }

    internal class IGTradeListener : SubscriptionListener
    {
        private readonly IGLightstreamerClient _client;

        public IGTradeListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void onItemUpdate(ItemUpdate update)
        {
            try
            {
                // Parse trade confirmation JSON
                var confirms = update.getValue("CONFIRMS");
                if (!string.IsNullOrEmpty(confirms) && confirms != "null")
                {
                    // Parse and raise trade update
                    var json = JObject.Parse(confirms);
                    _client.RaiseTradeUpdate(
                        json["dealId"]?.ToString(),
                        json["dealStatus"]?.ToString(),
                        json["reason"]?.ToString(),
                        json["level"]?.Value<decimal>(),
                        json["size"]?.Value<decimal>()
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error($"IGTradeListener.onItemUpdate: Error: {ex.Message}");
            }
        }

        public void onItemLostUpdates(string itemName, int itemPos, int lostUpdates) { }
        public void onSubscription() { }
        public void onUnsubscription() { }
        public void onClearSnapshot(string itemName, int itemPos) { }
        public void onCommandSecondLevelItemLostUpdates(int lostUpdates, string key) { }
        public void onCommandSecondLevelSubscriptionError(int code, string message, string key) { }
        public void onEndOfSnapshot(string itemName, int itemPos) { }
        public void onListenEnd() { }
        public void onListenStart() { }
        public void onSubscriptionError(int code, string message) =>
            Log.Error($"IGTradeListener: Subscription error {code}: {message}");
        public void onRealMaxFrequency(string frequency) { }
    }

    internal class IGAccountListener : SubscriptionListener
    {
        private readonly IGLightstreamerClient _client;

        public IGAccountListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void onItemUpdate(ItemUpdate update)
        {
            try
            {
                var balance = ParseDecimal(update.getValue("FUNDS")) ?? 0;
                var margin = ParseDecimal(update.getValue("MARGIN")) ?? 0;
                var available = ParseDecimal(update.getValue("AVAILABLE_CASH")) ?? 0;
                var pnl = ParseDecimal(update.getValue("PNL")) ?? 0;

                _client.RaiseAccountUpdate("USD", balance, margin, available, pnl);
            }
            catch (Exception ex)
            {
                Log.Error($"IGAccountListener.onItemUpdate: Error: {ex.Message}");
            }
        }

        public void onItemLostUpdates(string itemName, int itemPos, int lostUpdates) { }
        public void onSubscription() { }
        public void onUnsubscription() { }
        public void onClearSnapshot(string itemName, int itemPos) { }
        public void onCommandSecondLevelItemLostUpdates(int lostUpdates, string key) { }
        public void onCommandSecondLevelSubscriptionError(int code, string message, string key) { }
        public void onEndOfSnapshot(string itemName, int itemPos) { }
        public void onListenEnd() { }
        public void onListenStart() { }
        public void onSubscriptionError(int code, string message) =>
            Log.Error($"IGAccountListener: Subscription error {code}: {message}");
        public void onRealMaxFrequency(string frequency) { }

        private decimal? ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null")
                return null;
            return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : (decimal?)null;
        }
    }

    #endregion

    #region Event Args

    public class IGPriceUpdateEventArgs : EventArgs
    {
        public string Epic { get; set; }
        public decimal? Bid { get; set; }
        public decimal? Ask { get; set; }
        public decimal? BidSize { get; set; }
        public decimal? AskSize { get; set; }
        public decimal? LastTradedPrice { get; set; }
        public long? LastTradedVolume { get; set; }
    }

    public class IGTradeUpdateEventArgs : EventArgs
    {
        public string DealId { get; set; }
        public string Status { get; set; }
        public string Reason { get; set; }
        public decimal? FilledPrice { get; set; }
        public decimal? FilledSize { get; set; }
    }

    public class IGAccountUpdateEventArgs : EventArgs
    {
        public string Currency { get; set; }
        public decimal Balance { get; set; }
        public decimal Margin { get; set; }
        public decimal AvailableCash { get; set; }
        public decimal PnL { get; set; }
    }

    public class IGStreamingErrorEventArgs : EventArgs
    {
        public int Code { get; set; }
        public string Message { get; set; }
    }

    #endregion
}
