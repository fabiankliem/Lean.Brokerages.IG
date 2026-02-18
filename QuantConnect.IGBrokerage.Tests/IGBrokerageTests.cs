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
        protected override Symbol Symbol => Symbol.Create("EURUSD", SecurityType.Forex, IGSymbolMapper.MarketName);
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
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var username = Config.Get("ig-username");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGBrokerageTests: Credentials not configured in config.json");
            }

            var brokerage = new IGBrokerage(apiUrl, username, password, apiKey, accountId, null);

            brokerage.Connect();
            Thread.Sleep(2000);

            if (!brokerage.IsConnected)
            {
                Assert.Fail("IGBrokerage failed to connect");
            }

            return brokerage;
        }

        protected override bool IsAsync()
        {
            return true;
        }

        protected override decimal GetAskPrice(Symbol symbol)
        {
            var brokerage = (IGBrokerage)Brokerage;
            return brokerage.GetCurrentAskPrice(symbol);
        }

        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static IEnumerable<TestCaseData> OrderParameters()
        {
            var eurusd = Symbol.Create("EURUSD", SecurityType.Forex, IGSymbolMapper.MarketName);
            yield return new TestCaseData(new MarketOrderTestParameters(eurusd));
            yield return new TestCaseData(new LimitOrderTestParameters(eurusd, 1.1500m, 1.0500m));
            yield return new TestCaseData(new StopMarketOrderTestParameters(eurusd, 1.1500m, 1.0500m));

            var gbpusd = Symbol.Create("GBPUSD", SecurityType.Forex, IGSymbolMapper.MarketName);
            yield return new TestCaseData(new StopLimitOrderTestParameters(gbpusd, 1.3500m, 1.2500m));

            var spx = Symbol.Create("SPX", SecurityType.Index, IGSymbolMapper.MarketName);
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
