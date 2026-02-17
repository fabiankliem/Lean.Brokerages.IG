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

using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.IG
{
    public partial class IGBrokerage
    {
        private bool _unsupportedTickResolutionWarningFired;
        private bool _unsupportedOpenInterestWarningFired;

        /// <summary>
        /// Gets historical data for the requested symbol
        /// </summary>
        public override IEnumerable<BaseData> GetHistory(HistoryRequest request)
        {
            if (!IGSymbolMapper.SupportedSecurityTypes.Contains(request.Symbol.SecurityType))
            {
                if (!_unsupportedTickResolutionWarningFired)
                {
                    _unsupportedTickResolutionWarningFired = true;
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedSecurityType",
                        $"IG does not support {request.Symbol.SecurityType} historical data"));
                }
                return null;
            }

            if (request.Resolution == Resolution.Tick)
            {
                if (!_unsupportedTickResolutionWarningFired)
                {
                    _unsupportedTickResolutionWarningFired = true;
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedResolution",
                        "IG does not support Tick resolution historical data. Use Second or higher."));
                }
                return null;
            }

            if (request.TickType == TickType.OpenInterest)
            {
                if (!_unsupportedOpenInterestWarningFired)
                {
                    _unsupportedOpenInterestWarningFired = true;
                    OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedTickType",
                        "IG does not support OpenInterest data."));
                }
                return null;
            }

            var epic = _symbolMapper.GetBrokerageSymbol(request.Symbol);
            if (string.IsNullOrEmpty(epic))
            {
                return null;
            }

            var resolution = ConvertResolution(request.Resolution);
            if (resolution == null)
            {
                return null;
            }

            return GetHistoryInternal(request, epic, resolution);
        }

        private IEnumerable<BaseData> GetHistoryInternal(HistoryRequest request, string epic, string resolution)
        {
            IEnumerable<Models.IGPriceCandleData> prices;
            try
            {
                prices = _restClient.GetHistoricalPrices(epic, resolution, request.StartTimeUtc, request.EndTimeUtc);
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetHistory(): Error fetching {epic}: {ex.Message}");
                yield break;
            }

            var (pipValue, _) = GetInstrumentConversion(epic);
            var period = request.Resolution.ToTimeSpan();

            foreach (var price in prices)
            {
                if (request.TickType == TickType.Quote)
                {
                    yield return GetQuoteBar(price, request.Symbol, pipValue, period);
                }
                else
                {
                    yield return GetTradeBar(price, request.Symbol, pipValue, period);
                }
            }
        }

        private static QuoteBar GetQuoteBar(Models.IGPriceCandleData candle, Symbol symbol,
            decimal pipValue, TimeSpan period)
        {
            return new QuoteBar
            {
                Symbol = symbol,
                Time = candle.SnapshotTime,
                Bid = new Bar(
                    (candle.OpenPrice?.Bid ?? 0) * pipValue,
                    (candle.HighPrice?.Bid ?? 0) * pipValue,
                    (candle.LowPrice?.Bid ?? 0) * pipValue,
                    (candle.ClosePrice?.Bid ?? 0) * pipValue),
                Ask = new Bar(
                    (candle.OpenPrice?.Ask ?? 0) * pipValue,
                    (candle.HighPrice?.Ask ?? 0) * pipValue,
                    (candle.LowPrice?.Ask ?? 0) * pipValue,
                    (candle.ClosePrice?.Ask ?? 0) * pipValue),
                Period = period
            };
        }

        private static TradeBar GetTradeBar(Models.IGPriceCandleData candle, Symbol symbol,
            decimal pipValue, TimeSpan period)
        {
            var openBid = candle.OpenPrice?.Bid ?? 0;
            var openAsk = candle.OpenPrice?.Ask ?? 0;
            var highBid = candle.HighPrice?.Bid ?? 0;
            var highAsk = candle.HighPrice?.Ask ?? 0;
            var lowBid = candle.LowPrice?.Bid ?? 0;
            var lowAsk = candle.LowPrice?.Ask ?? 0;
            var closeBid = candle.ClosePrice?.Bid ?? 0;
            var closeAsk = candle.ClosePrice?.Ask ?? 0;

            return new TradeBar
            {
                Symbol = symbol,
                Time = candle.SnapshotTime,
                Open = (openBid + openAsk) / 2 * pipValue,
                High = (highBid + highAsk) / 2 * pipValue,
                Low = (lowBid + lowAsk) / 2 * pipValue,
                Close = (closeBid + closeAsk) / 2 * pipValue,
                Volume = candle.LastTradedVolume ?? 0,
                Period = period
            };
        }

        private static string ConvertResolution(Resolution resolution)
        {
            return resolution switch
            {
                Resolution.Second => "SECOND",
                Resolution.Minute => "MINUTE",
                Resolution.Hour => "HOUR",
                Resolution.Daily => "DAY",
                _ => null
            };
        }
    }
}
