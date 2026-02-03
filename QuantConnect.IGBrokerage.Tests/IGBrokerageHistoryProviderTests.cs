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
using System.Linq;
using System.Threading;
using NUnit.Framework;
using QuantConnect.Data;
using QuantConnect.Tests;
using QuantConnect.Logging;
using QuantConnect.Securities;
using QuantConnect.Data.Market;
using QuantConnect.Lean.Engine.HistoricalData;
using QuantConnect.Configuration;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials")]
    public class IGBrokerageHistoryProviderTests
    {
        private static TestCaseData[] TestParameters
        {
            get
            {
                TestGlobals.Initialize();

                return
                [
                    // Forex - IG's primary market
                    new TestCaseData(
                        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                        Resolution.Minute,
                        TimeSpan.FromHours(2),
                        TickType.Quote,
                        typeof(QuoteBar),
                        false),

                    new TestCaseData(
                        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                        Resolution.Hour,
                        TimeSpan.FromDays(5),
                        TickType.Quote,
                        typeof(QuoteBar),
                        false),

                    new TestCaseData(
                        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                        Resolution.Daily,
                        TimeSpan.FromDays(30),
                        TickType.Quote,
                        typeof(QuoteBar),
                        false),

                    new TestCaseData(
                        Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG),
                        Resolution.Minute,
                        TimeSpan.FromHours(2),
                        TickType.Trade,
                        typeof(TradeBar),
                        false),

                    // Index
                    new TestCaseData(
                        Symbol.Create("SPX", SecurityType.Index, Market.IG),
                        Resolution.Hour,
                        TimeSpan.FromDays(3),
                        TickType.Trade,
                        typeof(TradeBar),
                        false),

                    new TestCaseData(
                        Symbol.Create("FTSE", SecurityType.Index, Market.IG),
                        Resolution.Daily,
                        TimeSpan.FromDays(30),
                        TickType.Trade,
                        typeof(TradeBar),
                        false),

                    // Invalid test - Tick not supported
                    new TestCaseData(
                        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                        Resolution.Tick,
                        TimeSpan.FromMinutes(5),
                        TickType.Quote,
                        typeof(Tick),
                        true),

                    // Invalid test - Wrong market
                    new TestCaseData(
                        Symbols.SPY,
                        Resolution.Daily,
                        TimeSpan.FromDays(10),
                        TickType.Trade,
                        typeof(TradeBar),
                        true),
                ];
            }
        }

        [Test, TestCaseSource(nameof(TestParameters))]
        public void GetsHistory(Symbol symbol, Resolution resolution, TimeSpan period, TickType tickType, Type dataType, bool invalidRequest)
        {
            // Get configuration from config.json
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");
            var environment = Config.Get("ig-environment", "demo");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGBrokerageHistoryProviderTests: Credentials not configured in config.json");
            }

            // Create brokerage with proper initialization
            var brokerage = new IGBrokerage(
                apiUrl,
                apiKey,
                identifier,
                password,
                accountId,
                environment,
                null,
                null
            );

            brokerage.Connect();
            Thread.Sleep(1000); // Wait for connection

            if (!brokerage.IsConnected)
            {
                Assert.Fail("Failed to connect to IG Markets");
            }

            var historyProvider = new BrokerageHistoryProvider();
            historyProvider.SetBrokerage(brokerage);
            historyProvider.Initialize(new HistoryProviderInitializeParameters(
                null, null, null, null, null, null, null,
                false, null, null, new AlgorithmSettings()));

            var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
            var now = DateTime.UtcNow;
            var requests = new[]
            {
                new HistoryRequest(
                    now.Add(-period),
                    now,
                    dataType,
                    symbol,
                    resolution,
                    marketHoursDatabase.GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType),
                    marketHoursDatabase.GetDataTimeZone(symbol.ID.Market, symbol, symbol.SecurityType),
                    resolution,
                    false,
                    false,
                    DataNormalizationMode.Adjusted,
                    tickType)
            };

            var historyArray = historyProvider.GetHistory(requests, TimeZones.Utc)?.ToArray();

            if (invalidRequest)
            {
                Assert.IsNull(historyArray);
                return;
            }

            Assert.IsNotNull(historyArray);
            foreach (var slice in historyArray)
            {
                if (resolution == Resolution.Tick)
                {
                    foreach (var tick in slice.Ticks[symbol])
                    {
                        Log.Debug($"{tick}");
                    }
                }
                else if (slice.QuoteBars.TryGetValue(symbol, out var quoteBar))
                {
                    Log.Debug($"{quoteBar}");
                }
                else if (slice.Bars.TryGetValue(symbol, out var tradeBar))
                {
                    Log.Debug($"{tradeBar}");
                }
            }

            if (historyProvider.DataPointCount > 0)
            {
                // Ordered by time
                Assert.That(historyArray, Is.Ordered.By("Time"));

                // No repeating bars
                var timesArray = historyArray.Select(x => x.Time).ToArray();
                Assert.AreEqual(timesArray.Length, timesArray.Distinct().Count());
            }

            Log.Trace("Data points retrieved: " + historyProvider.DataPointCount);

            // Cleanup
            brokerage.Disconnect();
            brokerage.Dispose();
        }
    }
}