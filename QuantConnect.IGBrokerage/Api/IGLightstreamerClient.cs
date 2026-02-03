using Lightstreamer.DotNet.Client;
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

        private LSClient _client;
        private readonly object _lock = new object();
        private volatile bool _isConnected;

        // Event handlers
        public event EventHandler<IGPriceUpdateEventArgs> OnPriceUpdate;
        public event EventHandler<IGTradeUpdateEventArgs> OnTradeUpdate;
        public event EventHandler<IGAccountUpdateEventArgs> OnAccountUpdate;
        public event EventHandler<IGStreamingErrorEventArgs> OnError;
        public event EventHandler OnDisconnect;

        // Subscription tracking
        private readonly Dictionary<string, SubscribedTableKey> _priceSubscriptions;
        private SubscribedTableKey _tradeSubscription;
        private SubscribedTableKey _accountSubscription;

        public IGLightstreamerClient(string endpoint, string cst, string securityToken, string accountId)
        {
            _endpoint = endpoint;
            _cst = cst;
            _securityToken = securityToken;
            _accountId = accountId;

            _priceSubscriptions = new Dictionary<string, SubscribedTableKey>();
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

                    _client = new LSClient();

                    var connectionInfo = new ConnectionInfo
                    {
                        PushServerUrl = _endpoint,
                        Adapter = "DEFAULT",
                        User = _accountId,
                        Password = $"CST-{_cst}|XST-{_securityToken}"
                    };

                    _client.OpenConnection(connectionInfo, new IGConnectionListener(this));
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
                        try { _client.UnsubscribeTable(sub); } catch { }
                    }
                    _priceSubscriptions.Clear();

                    if (_tradeSubscription != null)
                    {
                        try { _client.UnsubscribeTable(_tradeSubscription); } catch { }
                        _tradeSubscription = null;
                    }

                    if (_accountSubscription != null)
                    {
                        try { _client.UnsubscribeTable(_accountSubscription); } catch { }
                        _accountSubscription = null;
                    }

                    _client.CloseConnection();
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
                var tableInfo = new ExtendedTableInfo(
                    new[] { $"MARKET:{epic}" },
                    "MERGE",
                    new[] { "BID", "OFFER", "HIGH", "LOW", "MID_OPEN", "CHANGE", "CHANGE_PCT",
                            "UPDATE_TIME", "MARKET_STATE", "MARKET_DELAY" },
                    true
                );

                var listener = new IGPriceListener(epic, this);
                var tableKey = _client.SubscribeTable(tableInfo, listener, false);
                _priceSubscriptions[epic] = tableKey;

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
            if (_priceSubscriptions.TryGetValue(epic, out var tableKey))
            {
                try
                {
                    _client.UnsubscribeTable(tableKey);
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
                var tableInfo = new ExtendedTableInfo(
                    new[] { $"TRADE:{_accountId}" },
                    "DISTINCT",
                    new[] { "CONFIRMS", "OPU", "WOU" },
                    true
                );

                var listener = new IGTradeListener(this);
                _tradeSubscription = _client.SubscribeTable(tableInfo, listener, false);

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
                var tableInfo = new ExtendedTableInfo(
                    new[] { $"ACCOUNT:{_accountId}" },
                    "MERGE",
                    new[] { "PNL", "DEPOSIT", "AVAILABLE_CASH", "FUNDS", "MARGIN", "MARGIN_LR",
                            "MARGIN_NLR", "AVAILABLE_TO_DEAL", "EQUITY" },
                    true
                );

                var listener = new IGAccountListener(this);
                _accountSubscription = _client.SubscribeTable(tableInfo, listener, false);

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

        #endregion

        public void Dispose()
        {
            Disconnect();
        }
    }

    #region Lightstreamer Listeners

    internal class IGConnectionListener : IConnectionListener
    {
        private readonly IGLightstreamerClient _client;

        public IGConnectionListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void OnConnectionEstablished() =>
            Log.Trace("IGLightstreamer: Connection established");

        public void OnSessionStarted(bool isPolling, string controlLink) =>
            Log.Trace($"IGLightstreamer: Session started, polling={isPolling}");

        public void OnNewBytes(long bytes) { }

        public void OnDataError(PushServerException e) =>
            _client.RaiseError(-1, $"Data error: {e.Message}");

        public void OnActivityWarning(bool warningOn) =>
            Log.Trace($"IGLightstreamer: Activity warning={warningOn}");

        public void OnClose() => _client.RaiseDisconnect();

        public void OnEnd(int cause) =>
            Log.Trace($"IGLightstreamer: Connection ended, cause={cause}");

        public void OnFailure(PushServerException e) =>
            _client.RaiseError(-1, $"Connection failure: {e.Message}");

        public void OnFailure(PushConnException e) =>
            _client.RaiseError(-1, $"Connection failure: {e.Message}");
    }

    internal class IGPriceListener : IHandyTableListener
    {
        private readonly string _epic;
        private readonly IGLightstreamerClient _client;

        public IGPriceListener(string epic, IGLightstreamerClient client)
        {
            _epic = epic;
            _client = client;
        }

        public void OnUpdate(int itemPos, string itemName, IUpdateInfo update)
        {
            try
            {
                var bid = ParseDecimal(update.GetNewValue("BID"));
                var ask = ParseDecimal(update.GetNewValue("OFFER"));

                _client.RaisePriceUpdate(_epic, bid, ask, null, null, null, null);
            }
            catch (Exception ex)
            {
                Log.Error($"IGPriceListener.OnUpdate: Error: {ex.Message}");
            }
        }

        public void OnRawUpdatesLost(int itemPos, string itemName, int lostUpdates) =>
            Log.Warning($"IGPriceListener: Lost {lostUpdates} updates for {itemName}");

        public void OnSnapshotEnd(int itemPos, string itemName) { }
        public void OnUnsubscr(int itemPos, string itemName) { }
        public void OnUnsubscrAll() { }

        private decimal? ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null")
                return null;
            return decimal.TryParse(value, out var result) ? result : (decimal?)null;
        }
    }

    internal class IGTradeListener : IHandyTableListener
    {
        private readonly IGLightstreamerClient _client;

        public IGTradeListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void OnUpdate(int itemPos, string itemName, IUpdateInfo update)
        {
            try
            {
                // Parse trade confirmation JSON
                var confirms = update.GetNewValue("CONFIRMS");
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
                Log.Error($"IGTradeListener.OnUpdate: Error: {ex.Message}");
            }
        }

        public void OnRawUpdatesLost(int itemPos, string itemName, int lostUpdates) { }
        public void OnSnapshotEnd(int itemPos, string itemName) { }
        public void OnUnsubscr(int itemPos, string itemName) { }
        public void OnUnsubscrAll() { }
    }

    internal class IGAccountListener : IHandyTableListener
    {
        private readonly IGLightstreamerClient _client;

        public IGAccountListener(IGLightstreamerClient client)
        {
            _client = client;
        }

        public void OnUpdate(int itemPos, string itemName, IUpdateInfo update)
        {
            try
            {
                var balance = ParseDecimal(update.GetNewValue("FUNDS")) ?? 0;
                var margin = ParseDecimal(update.GetNewValue("MARGIN")) ?? 0;
                var available = ParseDecimal(update.GetNewValue("AVAILABLE_CASH")) ?? 0;
                var pnl = ParseDecimal(update.GetNewValue("PNL")) ?? 0;

                _client.RaiseAccountUpdate("USD", balance, margin, available, pnl);
            }
            catch (Exception ex)
            {
                Log.Error($"IGAccountListener.OnUpdate: Error: {ex.Message}");
            }
        }

        public void OnRawUpdatesLost(int itemPos, string itemName, int lostUpdates) { }
        public void OnSnapshotEnd(int itemPos, string itemName) { }
        public void OnUnsubscr(int itemPos, string itemName) { }
        public void OnUnsubscrAll() { }

        private decimal? ParseDecimal(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "null")
                return null;
            return decimal.TryParse(value, out var result) ? result : (decimal?)null;
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
