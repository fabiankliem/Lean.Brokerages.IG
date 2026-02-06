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
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Configuration;
using QuantConnect.Logging;
using QuantConnect.ToolBox;
using QuantConnect.Brokerages.IG.Api;

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
            // Get configuration from Config
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException(
                    "IGExchangeInfoDownloader requires ig-api-key, ig-identifier, and ig-password in config");
            }

            // Initialize REST client
            _apiClient = new IGRestApiClient(apiUrl, apiKey);

            // Authenticate
            try
            {
                var loginResponse = _apiClient.Login(identifier, password);
                _apiClient.SetSessionTokens(loginResponse.Cst, loginResponse.SecurityToken);
                Log.Trace("IGExchangeInfoDownloader: Successfully authenticated");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"IGExchangeInfoDownloader: Failed to authenticate: {ex.Message}", ex);
            }
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

            // Search for all major asset classes
            var searchTerms = new[]
            {
                // Forex - Major currencies
                "EUR", "GBP", "USD", "JPY", "AUD", "CAD", "CHF", "NZD",
                // Indices - Major global markets
                "FTSE", "DAX", "CAC", "SPX", "DOW", "NASDAQ", "NIKKEI", "HANGSENG",
                "RUSSELL", "STOXX", "IBEX", "ASX", "KOSPI",
                // Commodities - Energy & Metals
                "GOLD", "SILVER", "OIL", "GAS", "COPPER", "PLATINUM", "PALLADIUM",
                // Commodities - Agriculture
                "CORN", "WHEAT", "SOYBEAN", "COFFEE", "SUGAR", "COTTON", "COCOA",
                // Crypto
                "BITCOIN", "ETHEREUM", "RIPPLE", "LITECOIN", "CARDANO", "SOLANA", "POLKADOT"
            };

            var instruments = new HashSet<string>(); // Prevent duplicates
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
                            continue; // Skip duplicates

                        instruments.Add(market.Epic);

                        // Convert to CSV format
                        var csvLine = ConvertToSymbolPropertiesFormat(market);
                        if (!string.IsNullOrEmpty(csvLine))
                        {
                            processedCount++;
                            results.Add(csvLine);
                        }
                    }

                    // Rate limiting: respect IG's 60 requests/minute limit
                    System.Threading.Thread.Sleep(1100); // ~54 requests/minute
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
        /// Converts IG market info to symbol properties CSV format
        /// </summary>
        private string ConvertToSymbolPropertiesFormat(dynamic market)
        {
            try
            {
                // Extract data from IG market response
                var epic = (string)market.Epic;
                var instrumentName = (string)market.InstrumentName;
                var instrumentType = (string)market.InstrumentType;

                // Map IG instrument type to LEAN SecurityType
                var securityType = MapInstrumentTypeToSecurityType(instrumentType);
                if (string.IsNullOrEmpty(securityType))
                    return null; // Skip unsupported types

                // Extract market details (with defaults for missing fields)
                var lotSize = market.LotSize != null ? (decimal)market.LotSize : 1m;
                var minDealSize = market.MinDealSize != null ? (decimal)market.MinDealSize : 0.01m;
                var scalingFactor = market.ScalingFactor != null ? (decimal)market.ScalingFactor : 1m;

                // Determine quote currency (default to USD for most CFDs)
                var quoteCurrency = DetermineQuoteCurrency(epic, instrumentType);

                // Calculate minimum price variation (pip/point size)
                var minPriceVariation = CalculateMinPriceVariation(instrumentType, scalingFactor);

                // Format: market,symbol,type,description,quote_currency,contract_multiplier,
                //         minimum_price_variation,lot_size,market_ticker,minimum_order_size,
                //         price_magnifier,strike_multiplier
                return $"ig,{epic},{securityType},{EscapeCsv(instrumentName)}," +
                       $"{quoteCurrency},{scalingFactor},{minPriceVariation},{lotSize},,{minDealSize},,";
            }
            catch (Exception ex)
            {
                Log.Error($"IGExchangeInfoDownloader: Error converting market data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps IG instrument type to LEAN SecurityType
        /// </summary>
        private string MapInstrumentTypeToSecurityType(string instrumentType)
        {
            if (string.IsNullOrEmpty(instrumentType))
                return "cfd";

            switch (instrumentType.ToUpperInvariant())
            {
                case "CURRENCIES":
                    return "forex";
                case "INDICES":
                    return "index";
                case "COMMODITIES":
                    return "cfd";
                case "SHARES":
                case "EQUITIES":
                    return "equity";
                case "CRYPTOCURRENCIES":
                    return "crypto";
                case "OPTIONS":
                    return "option";
                default:
                    return "cfd"; // Default to CFD for unknown types
            }
        }

        /// <summary>
        /// Determines quote currency from EPIC and instrument type
        /// </summary>
        private string DetermineQuoteCurrency(string epic, string instrumentType)
        {
            // Forex pairs: extract quote currency from EPIC
            if (instrumentType?.ToUpperInvariant() == "CURRENCIES" && epic.Length >= 9)
            {
                // Format: CS.D.EURUSD.MINI.IP -> USD is quote
                var parts = epic.Split('.');
                if (parts.Length >= 3)
                {
                    var pairPart = parts[2];
                    if (pairPart.Length >= 6)
                        return pairPart.Substring(3, 3); // Last 3 chars of pair
                }
            }

            // UK instruments typically in GBP
            if (epic.Contains("FTSE") || epic.Contains("UK"))
                return "GBP";

            // European instruments in EUR
            if (epic.Contains("DAX") || epic.Contains("CAC") || epic.Contains("EURO"))
                return "EUR";

            // Default to USD for most CFDs and indices
            return "USD";
        }

        /// <summary>
        /// Calculates minimum price variation based on instrument type
        /// </summary>
        private decimal CalculateMinPriceVariation(string instrumentType, decimal scalingFactor)
        {
            switch (instrumentType?.ToUpperInvariant())
            {
                case "CURRENCIES":
                    return 0.00001m; // 0.1 pip for forex
                case "INDICES":
                    return scalingFactor > 0 ? 1m / scalingFactor : 0.1m; // Typically 0.1 or 1 point
                case "COMMODITIES":
                    return 0.01m; // 1 cent for most commodities
                case "CRYPTOCURRENCIES":
                    return 0.01m; // 1 cent for crypto CFDs
                default:
                    return 0.01m;
            }
        }

        /// <summary>
        /// Escapes CSV special characters
        /// </summary>
        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            // Replace commas, quotes, and newlines
            return value.Replace(",", ";").Replace("\"", "").Replace("\n", " ").Replace("\r", "");
        }
    }
}
