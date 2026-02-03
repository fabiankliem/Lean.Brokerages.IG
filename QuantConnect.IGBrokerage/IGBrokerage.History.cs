using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.IG
{
    public partial class IGBrokerage
    {
        /// <summary>
        /// Gets historical data for the requested symbol
        /// </summary>
        public override IEnumerable<BaseData> GetHistory(HistoryRequest request)
        {
            if (!CanSubscribe(request.Symbol))
            {
                Log.Trace($"IGBrokerage.GetHistory(): Cannot get history for {request.Symbol}");
                return null;
            }

            var epic = _symbolMapper.GetBrokerageSymbol(request.Symbol);
            var resolution = ConvertResolution(request.Resolution);

            if (resolution == null)
            {
                Log.Trace($"IGBrokerage.GetHistory(): Resolution {request.Resolution} not supported");
                return null;
            }

            _nonTradingRateGate.WaitToProceed();

            try
            {
                Log.Trace($"IGBrokerage.GetHistory(): Fetching {request.Resolution} history for {epic} " +
                          $"from {request.StartTimeUtc} to {request.EndTimeUtc}");

                var prices = _restClient.GetHistoricalPrices(
                    epic,
                    resolution,
                    request.StartTimeUtc,
                    request.EndTimeUtc
                );

                var result = new List<BaseData>();

                foreach (var price in prices)
                {
                    BaseData data;

                    if (request.TickType == TickType.Quote)
                    {
                        data = new QuoteBar
                        {
                            Symbol = request.Symbol,
                            Time = price.SnapshotTime,
                            Bid = new Bar(price.OpenBid, price.HighBid, price.LowBid, price.CloseBid),
                            Ask = new Bar(price.OpenAsk, price.HighAsk, price.LowAsk, price.CloseAsk),
                            Period = request.Resolution.ToTimeSpan()
                        };
                    }
                    else
                    {
                        // Trade bar - use mid prices
                        data = new TradeBar
                        {
                            Symbol = request.Symbol,
                            Time = price.SnapshotTime,
                            Open = (price.OpenBid + price.OpenAsk) / 2,
                            High = (price.HighBid + price.HighAsk) / 2,
                            Low = (price.LowBid + price.LowAsk) / 2,
                            Close = (price.CloseBid + price.CloseAsk) / 2,
                            Volume = price.Volume ?? 0,
                            Period = request.Resolution.ToTimeSpan()
                        };
                    }

                    result.Add(data);
                }

                Log.Trace($"IGBrokerage.GetHistory(): Retrieved {result.Count} bars");
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"IGBrokerage.GetHistory(): Error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts LEAN resolution to IG resolution string
        /// </summary>
        private string ConvertResolution(Resolution resolution)
        {
            switch (resolution)
            {
                case Resolution.Second:
                    return "SECOND"; // IG supports SECOND for some instruments
                case Resolution.Minute:
                    return "MINUTE";
                case Resolution.Hour:
                    return "HOUR";
                case Resolution.Daily:
                    return "DAY";
                default:
                    return null; // Tick not directly supported
            }
        }
    }
}
