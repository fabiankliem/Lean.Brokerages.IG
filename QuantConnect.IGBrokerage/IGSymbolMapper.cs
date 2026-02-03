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
using QuantConnect.Brokerages;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// Provides symbol mapping between LEAN symbols and IG Markets EPIC codes
    /// </summary>
    /// <remarks>
    /// IG uses EPIC codes to identify instruments. The format is typically:
    /// - Forex: CS.D.{BASE}{QUOTE}.{CONTRACT}.IP (e.g., CS.D.EURUSD.MINI.IP)
    /// - Indices: IX.D.{INDEX}.{CONTRACT}.IP (e.g., IX.D.FTSE.DAILY.IP)
    /// - Commodities: CC.D.{COMMODITY}.{TYPE}.IP (e.g., CC.D.CL.USS.IP)
    /// - Crypto: CS.D.{CRYPTO}.CFD.IP (e.g., CS.D.BITCOIN.CFD.IP)
    /// </remarks>
    public class IGSymbolMapper : ISymbolMapper
    {
        // Common Forex pair mappings
        private static readonly Dictionary<string, string> ForexEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "EURUSD", "CS.D.EURUSD.MINI.IP" },
            { "GBPUSD", "CS.D.GBPUSD.MINI.IP" },
            { "USDJPY", "CS.D.USDJPY.MINI.IP" },
            { "USDCHF", "CS.D.USDCHF.MINI.IP" },
            { "AUDUSD", "CS.D.AUDUSD.MINI.IP" },
            { "USDCAD", "CS.D.USDCAD.MINI.IP" },
            { "NZDUSD", "CS.D.NZDUSD.MINI.IP" },
            { "EURGBP", "CS.D.EURGBP.MINI.IP" },
            { "EURJPY", "CS.D.EURJPY.MINI.IP" },
            { "GBPJPY", "CS.D.GBPJPY.MINI.IP" },
        };

        // Common Index mappings
        private static readonly Dictionary<string, string> IndexEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SPX", "IX.D.SPTRD.DAILY.IP" },       // S&P 500
            { "NDX", "IX.D.NASDAQ.DAILY.IP" },      // NASDAQ 100
            { "DJI", "IX.D.DOW.DAILY.IP" },         // Dow Jones
            { "FTSE", "IX.D.FTSE.DAILY.IP" },       // FTSE 100
            { "DAX", "IX.D.DAX.DAILY.IP" },         // DAX
            { "CAC", "IX.D.CAC.DAILY.IP" },         // CAC 40
            { "N225", "IX.D.NIKKEI.DAILY.IP" },     // Nikkei 225
            { "HSI", "IX.D.HANGSENG.DAILY.IP" },    // Hang Seng
        };

        // Common Crypto mappings
        private static readonly Dictionary<string, string> CryptoEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BTCUSD", "CS.D.BITCOIN.CFD.IP" },
            { "ETHUSD", "CS.D.ETHUSD.CFD.IP" },
            { "LTCUSD", "CS.D.LTCUSD.CFD.IP" },
            { "XRPUSD", "CS.D.XRPUSD.CFD.IP" },
        };

        // Common Commodity mappings
        private static readonly Dictionary<string, string> CommodityEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "XAUUSD", "CS.D.USCGC.TODAY.IP" },    // Gold
            { "XAGUSD", "CS.D.USCSI.TODAY.IP" },    // Silver
            { "CL", "CC.D.CL.USS.IP" },             // Crude Oil
            { "NG", "CC.D.NG.USS.IP" },             // Natural Gas
        };

        // Reverse mapping from EPIC to symbol info
        private static readonly Dictionary<string, (string Symbol, SecurityType SecurityType)> EpicToSymbolMap;

        static IGSymbolMapper()
        {
            EpicToSymbolMap = new Dictionary<string, (string, SecurityType)>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in ForexEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Forex);
            }
            foreach (var kvp in IndexEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Index);
            }
            foreach (var kvp in CryptoEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Crypto);
            }
            foreach (var kvp in CommodityEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Cfd);
            }
        }

        /// <summary>
        /// Converts a LEAN symbol to an IG EPIC code
        /// </summary>
        /// <param name="symbol">The LEAN symbol</param>
        /// <returns>The IG EPIC code</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            if (symbol == null)
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            var ticker = symbol.Value;

            switch (symbol.SecurityType)
            {
                case SecurityType.Forex:
                    if (ForexEpicMap.TryGetValue(ticker, out var forexEpic))
                    {
                        return forexEpic;
                    }
                    // Try to construct EPIC for unknown forex pairs
                    return $"CS.D.{ticker}.MINI.IP";

                case SecurityType.Index:
                    if (IndexEpicMap.TryGetValue(ticker, out var indexEpic))
                    {
                        return indexEpic;
                    }
                    break;

                case SecurityType.Crypto:
                    if (CryptoEpicMap.TryGetValue(ticker, out var cryptoEpic))
                    {
                        return cryptoEpic;
                    }
                    // Try to construct EPIC for unknown crypto
                    return $"CS.D.{ticker}.CFD.IP";

                case SecurityType.Cfd:
                case SecurityType.Equity:
                    if (CommodityEpicMap.TryGetValue(ticker, out var commodityEpic))
                    {
                        return commodityEpic;
                    }
                    break;
            }

            // Return null if no mapping found - caller should handle via API lookup
            return null;
        }

        /// <summary>
        /// Converts an IG EPIC code to a LEAN symbol
        /// </summary>
        /// <param name="brokerageSymbol">The IG EPIC code</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">The expiration date (for futures/options)</param>
        /// <param name="strike">The strike price (for options)</param>
        /// <param name="optionRight">The option right (for options)</param>
        /// <returns>The LEAN symbol</returns>
        public Symbol GetLeanSymbol(
            string brokerageSymbol,
            SecurityType securityType,
            string market,
            DateTime expirationDate = default,
            decimal strike = 0,
            OptionRight optionRight = OptionRight.Call)
        {
            if (string.IsNullOrEmpty(brokerageSymbol))
            {
                throw new ArgumentNullException(nameof(brokerageSymbol));
            }

            // Try to find in reverse mapping
            if (EpicToSymbolMap.TryGetValue(brokerageSymbol, out var symbolInfo))
            {
                return Symbol.Create(symbolInfo.Symbol, symbolInfo.SecurityType, market);
            }

            // Try to parse EPIC code
            // Format: PREFIX.D.INSTRUMENT.CONTRACT.IP
            var parts = brokerageSymbol.Split('.');
            if (parts.Length >= 3)
            {
                var prefix = parts[0];
                var instrument = parts.Length >= 4 ? parts[2] : parts[1];

                // Determine security type from prefix
                var inferredSecurityType = securityType;
                if (securityType == SecurityType.Base)
                {
                    inferredSecurityType = prefix switch
                    {
                        "CS" when instrument.Contains("USD") && instrument.Length == 6 => SecurityType.Forex,
                        "CS" when instrument.Contains("BITCOIN") || instrument.Contains("ETH") => SecurityType.Crypto,
                        "IX" => SecurityType.Index,
                        "CC" => SecurityType.Cfd,
                        _ => SecurityType.Cfd
                    };
                }

                return Symbol.Create(instrument, inferredSecurityType, market);
            }

            // Fallback: create symbol directly from EPIC
            return Symbol.Create(brokerageSymbol, securityType, market);
        }

        /// <summary>
        /// Adds or updates a custom EPIC mapping
        /// </summary>
        /// <param name="leanSymbol">The LEAN symbol ticker</param>
        /// <param name="epic">The IG EPIC code</param>
        /// <param name="securityType">The security type</param>
        public void AddMapping(string leanSymbol, string epic, SecurityType securityType)
        {
            switch (securityType)
            {
                case SecurityType.Forex:
                    ForexEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Index:
                    IndexEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Crypto:
                    CryptoEpicMap[leanSymbol] = epic;
                    break;
                default:
                    CommodityEpicMap[leanSymbol] = epic;
                    break;
            }

            EpicToSymbolMap[epic] = (leanSymbol, securityType);
        }
    }
}
