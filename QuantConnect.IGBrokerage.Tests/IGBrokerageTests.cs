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
using System.Threading;
using NUnit.Framework;
using QuantConnect.Tests;
using QuantConnect.Interfaces;
using QuantConnect.Securities;
using System.Collections.Generic;
using QuantConnect.Tests.Brokerages;
using QuantConnect.Configuration;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials")]
    public partial class IGBrokerageTests : BrokerageTests
    {
        protected override Symbol Symbol => Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
        protected override SecurityType SecurityType => SecurityType.Forex;

        [SetUp]
        public void Setup()
        {
            Log.DebuggingEnabled = true;
            Log.DebuggingLevel = 1;
        }

        [TearDown]
        public void TearDown()
        {
            if (Brokerage != null && Brokerage.IsConnected)
            {
                Brokerage.Disconnect();
                Brokerage.Dispose();
            }
        }

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
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
                Assert.Ignore("IGBrokerageTests: Credentials not configured in config.json");
            }

            // Create brokerage instance
            var brokerage = new IGBrokerage(
                apiUrl,
                apiKey,
                identifier,
                password,
                accountId,
                environment,
                orderProvider,
                securityProvider
            );

            // Connect to IG
            brokerage.Connect();

            // Wait for connection
            Thread.Sleep(2000);

            if (!brokerage.IsConnected)
            {
                Assert.Fail("IGBrokerage failed to connect");
            }

            return brokerage;
        }

        protected override bool IsAsync()
        {
            // IG uses Lightstreamer for real-time updates
            // Order events come asynchronously via WebSocket
            return true;
        }

        protected override decimal GetAskPrice(Symbol symbol)
        {
            // Get current market data for the symbol
            var brokerage = (IGBrokerage)Brokerage;
            var epic = brokerage.SymbolMapper.GetBrokerageSymbol(symbol);

            if (string.IsNullOrEmpty(epic))
            {
                Assert.Fail($"Cannot map symbol {symbol} to IG EPIC");
            }

            try
            {
                // Use IG REST API to get current prices
                var marketData = brokerage.GetMarketData(epic);

                // Return offer (ask) price
                return marketData.Offer;
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to get ask price for {symbol}: {ex.Message}");
                return 0; // Never reached
            }
        }


        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static IEnumerable<TestCaseData> OrderParameters()
        {
            // Use forex pairs that IG definitely supports
            var eurusd = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            yield return new TestCaseData(new MarketOrderTestParameters(eurusd));
            yield return new TestCaseData(new LimitOrderTestParameters(eurusd, 1.1500m, 1.0500m));
            yield return new TestCaseData(new StopMarketOrderTestParameters(eurusd, 1.1500m, 1.0500m));

            var gbpusd = Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG);
            yield return new TestCaseData(new StopLimitOrderTestParameters(gbpusd, 1.3500m, 1.2500m));

            // IG supports indices
            var spx = Symbol.Create("SPX", SecurityType.Index, Market.IG);
            yield return new TestCaseData(new MarketOrderTestParameters(spx));
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            base.LongFromShort(parameters);
        }
    }
}