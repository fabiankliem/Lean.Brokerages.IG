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
using System.Collections.Generic;
using QuantConnect.Configuration;
using QuantConnect.Data;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG.ToolBox
{
    /// <summary>
    /// IG Brokerage Data Downloader implementation
    /// Downloads historical data from IG Markets for offline use
    /// </summary>
    public class IGBrokerageDownloader : IDataDownloader, IDisposable
    {
        private readonly IGBrokerage _brokerage;
        private readonly MarketHoursDatabase _marketHoursDatabase;

        /// <summary>
        /// Initializes the IG Brokerage Downloader
        /// </summary>
        public IGBrokerageDownloader()
        {
            Log.Trace("IGBrokerageDownloader: Initializing...");

            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var username = Config.Get("ig-username");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "IGBrokerageDownloader requires ig-api-key, ig-username, and ig-password in config");
            }

            _brokerage = new IGBrokerage(apiUrl, username, password, apiKey, accountId, null);

            _brokerage.Message += (sender, e) =>
            {
                if (e.Type == BrokerageMessageType.Error)
                {
                    Log.Error($"IGBrokerageDownloader: {e.Message}");
                }
                else
                {
                    Log.Trace($"IGBrokerageDownloader: {e.Message}");
                }
            };

            _brokerage.Connect();
            Log.Trace("IGBrokerageDownloader: Successfully connected to IG Markets");

            _marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
        }

        /// <summary>
        /// Get historical data enumerable for a single symbol, type and resolution
        /// given this start and end time (in UTC).
        /// </summary>
        /// <param name="dataDownloaderGetParameters">model class for passing in parameters for historical data</param>
        /// <returns>Enumerable of base data for this symbol</returns>
        public IEnumerable<BaseData> Get(DataDownloaderGetParameters dataDownloaderGetParameters)
        {
            var symbol = dataDownloaderGetParameters.Symbol;
            var resolution = dataDownloaderGetParameters.Resolution;
            var startUtc = dataDownloaderGetParameters.StartUtc;
            var endUtc = dataDownloaderGetParameters.EndUtc;
            var tickType = dataDownloaderGetParameters.TickType;

            Log.Trace($"IGBrokerageDownloader.Get(): Downloading {symbol} {resolution} from {startUtc:yyyy-MM-dd} to {endUtc:yyyy-MM-dd}");

            if (symbol == null)
            {
                Log.Error("IGBrokerageDownloader.Get(): Symbol cannot be null");
                yield break;
            }

            if (startUtc >= endUtc)
            {
                Log.Error($"IGBrokerageDownloader.Get(): Invalid date range: {startUtc} to {endUtc}");
                yield break;
            }

            if (symbol.IsCanonical())
            {
                Log.Trace("IGBrokerageDownloader.Get(): Canonical symbols not supported for IG Markets");
                yield break;
            }

            var dataType = LeanData.GetDataType(resolution, tickType);

            var exchangeHours = _marketHoursDatabase.GetExchangeHours(
                symbol.ID.Market, symbol, symbol.SecurityType);
            var dataTimeZone = _marketHoursDatabase.GetDataTimeZone(
                symbol.ID.Market, symbol, symbol.SecurityType);

            var request = new HistoryRequest(
                startUtc,
                endUtc,
                dataType,
                symbol,
                resolution,
                exchangeHours,
                dataTimeZone,
                resolution,
                includeExtendedMarketHours: resolution != Resolution.Hour && resolution != Resolution.Daily,
                isCustomData: false,
                dataNormalizationMode: DataNormalizationMode.Raw,
                tickType: tickType
            );

            IEnumerable<BaseData> history;
            try
            {
                history = _brokerage.GetHistory(request);
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerageDownloader.Get(): Error getting history for {symbol}: {ex.Message}");
                yield break;
            }

            if (history == null)
            {
                Log.Trace($"IGBrokerageDownloader.Get(): No data available for {symbol}");
                yield break;
            }

            var dataPointCount = 0;
            foreach (var dataPoint in history)
            {
                dataPointCount++;
                yield return dataPoint;
            }

            Log.Trace($"IGBrokerageDownloader.Get(): Downloaded {dataPointCount} data points for {symbol}");
        }

        /// <summary>
        /// Disposes of the downloader and releases resources
        /// </summary>
        public void Dispose()
        {
            Log.Trace("IGBrokerageDownloader: Disposing...");
            _brokerage?.Disconnect();
            _brokerage?.Dispose();
        }
    }
}
