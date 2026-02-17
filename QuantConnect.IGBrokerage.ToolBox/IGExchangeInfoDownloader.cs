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
using QuantConnect.Logging;
using QuantConnect.ToolBox;
using QuantConnect.Brokerages.IG.Api;
using QuantConnect.Brokerages.IG.Models;

namespace QuantConnect.Brokerages.IG.ToolBox
{
    /// <summary>
    /// IG Markets implementation of IExchangeInfoDownloader
    /// Downloads comprehensive instrument universe from IG Markets API
    /// </summary>
    public class IGExchangeInfoDownloader : IExchangeInfoDownloader
    {
        private readonly IGRestApiClient _apiClient;

        /// <summary>
        /// Market identifier
        /// </summary>
        public string Market => "ig";

        /// <summary>
        /// Initializes the IG Exchange Info Downloader
        /// </summary>
        public IGExchangeInfoDownloader()
        {
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var username = Config.Get("ig-username");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id", "");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "IGExchangeInfoDownloader requires ig-api-key, ig-username, and ig-password in config");
            }

            _apiClient = new IGRestApiClient(apiUrl, apiKey, accountId);
            _apiClient.Login(username, password);
            Log.Trace("IGExchangeInfoDownloader: Successfully authenticated");
        }

        /// <summary>
        /// Get exchange info comma-separated data
        /// Format: market,symbol,type,description,quote_currency,contract_multiplier,
        ///         minimum_price_variation,lot_size,market_ticker,minimum_order_size,
        ///         price_magnifier,strike_multiplier
        /// </summary>
        public IEnumerable<string> Get()
        {
            Log.Trace("IGExchangeInfoDownloader.Get(): Starting download of IG Markets instrument universe");

            var searchTerms = new[]
            {
                "EUR", "GBP", "USD", "JPY", "AUD", "CAD", "CHF", "NZD",
                "FTSE", "DAX", "CAC", "SPX", "DOW", "NASDAQ", "NIKKEI", "HANGSENG",
                "RUSSELL", "STOXX", "IBEX", "ASX", "KOSPI",
                "GOLD", "SILVER", "OIL", "GAS", "COPPER", "PLATINUM", "PALLADIUM",
                "CORN", "WHEAT", "SOYBEAN", "COFFEE", "SUGAR", "COTTON", "COCOA",
                "BITCOIN", "ETHEREUM", "RIPPLE", "LITECOIN", "CARDANO", "SOLANA", "POLKADOT"
            };

            var instruments = new HashSet<string>();
            var processedCount = 0;
            var results = new List<string>();

            foreach (var searchTerm in searchTerms)
            {
                try
                {
                    Log.Trace($"IGExchangeInfoDownloader.Get(): Searching for '{searchTerm}'...");
                    var markets = _apiClient.SearchMarkets(searchTerm);

                    foreach (var market in markets)
                    {
                        if (instruments.Contains(market.Epic))
                            continue;

                        instruments.Add(market.Epic);

                        var csvLine = ConvertToSymbolPropertiesFormat(market);
                        if (!string.IsNullOrEmpty(csvLine))
                        {
                            processedCount++;
                            results.Add(csvLine);
                        }
                    }

                    // Rate limiting: respect IG's 60 requests/minute limit
                    System.Threading.Thread.Sleep(1100);
                }
                catch (Exception ex)
                {
                    Log.Error($"IGExchangeInfoDownloader.Get(): Error searching '{searchTerm}': {ex.Message}");
                }
            }

            Log.Trace($"IGExchangeInfoDownloader.Get(): Downloaded {processedCount} instruments");

            foreach (var result in results)
            {
                yield return result;
            }
        }

        /// <summary>
        /// Converts IG market search result to symbol properties CSV format
        /// </summary>
        private string ConvertToSymbolPropertiesFormat(IGMarketSearchResult market)
        {
            var epic = market.Epic;
            var instrumentName = market.InstrumentName;
            var instrumentType = market.InstrumentType;

            var securityType = MapInstrumentTypeToSecurityType(instrumentType);
            if (string.IsNullOrEmpty(securityType))
                return null;

            var quoteCurrency = DetermineQuoteCurrency(epic, instrumentType);
            var minPriceVariation = CalculateMinPriceVariation(instrumentType);

            // Format: market,symbol,type,description,quote_currency,contract_multiplier,
            //         minimum_price_variation,lot_size,market_ticker,minimum_order_size,
            //         price_magnifier,strike_multiplier
            return $"ig,{epic},{securityType},{EscapeCsv(instrumentName)}," +
                   $"{quoteCurrency},1,{minPriceVariation},1,,0.01,,";
        }

        private static string MapInstrumentTypeToSecurityType(string instrumentType)
        {
            if (string.IsNullOrEmpty(instrumentType))
                return "cfd";

            return instrumentType.ToUpperInvariant() switch
            {
                "CURRENCIES" => "forex",
                "INDICES" => "index",
                "COMMODITIES" => "cfd",
                "SHARES" or "EQUITIES" => "equity",
                "CRYPTOCURRENCIES" => "crypto",
                "OPTIONS" => "option",
                _ => "cfd"
            };
        }

        private static string DetermineQuoteCurrency(string epic, string instrumentType)
        {
            if (instrumentType?.ToUpperInvariant() == "CURRENCIES")
            {
                var parts = epic.Split('.');
                if (parts.Length >= 3 && parts[2].Length >= 6)
                    return parts[2].Substring(3, 3);
            }

            if (epic.Contains("FTSE") || epic.Contains("UK"))
                return "GBP";

            if (epic.Contains("DAX") || epic.Contains("CAC") || epic.Contains("EURO"))
                return "EUR";

            return "USD";
        }

        private static decimal CalculateMinPriceVariation(string instrumentType)
        {
            return instrumentType?.ToUpperInvariant() switch
            {
                "CURRENCIES" => 0.00001m,
                "INDICES" => 0.1m,
                _ => 0.01m
            };
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace(",", ";").Replace("\"", "").Replace("\n", " ").Replace("\r", "");
        }
    }
}
