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
using System.Linq;
using System.Threading;
using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Logging;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials")]
    public class IGBrokerageCoreMethodsTests
    {
        private IGBrokerage _brokerage;

        [SetUp]
        public void Setup()
        {
            Log.DebuggingEnabled = true;
            Log.DebuggingLevel = 1;

            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var username = Config.Get("ig-username");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGBrokerageCoreMethodsTests: Credentials not configured in config.json");
            }

            _brokerage = new IGBrokerage(apiUrl, username, password, apiKey, accountId, null);
            _brokerage.Connect();
            Thread.Sleep(1000);

            if (!_brokerage.IsConnected)
            {
                Assert.Fail("Failed to connect to IG Markets");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_brokerage != null && _brokerage.IsConnected)
            {
                _brokerage.Disconnect();
                _brokerage.Dispose();
            }
        }

        #region GetCashBalance Tests

        [Test]
        public void GetCashBalance_ReturnsNonEmptyList()
        {
            var cashBalances = _brokerage.GetCashBalance();
            Assert.IsNotNull(cashBalances);
            Assert.IsNotEmpty(cashBalances);
        }

        [Test]
        public void GetCashBalance_ContainsValidCurrency()
        {
            var cashBalances = _brokerage.GetCashBalance();
            Assert.IsTrue(cashBalances.All(c => !string.IsNullOrEmpty(c.Currency)));
        }

        [Test]
        public void GetCashBalance_ContainsPositiveOrZeroAmount()
        {
            var cashBalances = _brokerage.GetCashBalance();
            Assert.IsTrue(cashBalances.All(c => c.Amount >= 0));
        }

        [Test]
        public void GetCashBalance_MultipleCallsReturnConsistentResults()
        {
            var balance1 = _brokerage.GetCashBalance();
            Thread.Sleep(500);
            var balance2 = _brokerage.GetCashBalance();
            Assert.AreEqual(balance1.Count, balance2.Count);
        }

        #endregion

        #region GetAccountHoldings Tests

        [Test]
        public void GetAccountHoldings_ReturnsListSuccessfully()
        {
            var holdings = _brokerage.GetAccountHoldings();
            Assert.IsNotNull(holdings);
        }

        [Test]
        public void GetAccountHoldings_WhenPositionsExist_ReturnsValidHoldings()
        {
            var holdings = _brokerage.GetAccountHoldings();
            foreach (var holding in holdings)
            {
                Assert.IsNotNull(holding.Symbol);
                Assert.AreNotEqual(0, holding.Quantity);
                Assert.Greater(holding.AveragePrice, 0);
                Assert.IsNotNull(holding.CurrencySymbol);
            }
        }

        #endregion

        #region PlaceOrder Tests

        [Test]
        public void PlaceOrder_MarketOrder_PlacesSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow);

            var result = _brokerage.PlaceOrder(order);
            Assert.IsTrue(result);

            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        [Test]
        public void PlaceOrder_LimitOrder_PlacesSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            var result = _brokerage.PlaceOrder(order);
            Assert.IsTrue(result);

            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void PlaceOrder_StopMarketOrder_PlacesSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new StopMarketOrder(symbol, 1000, 1.1500m, DateTime.UtcNow);

            var result = _brokerage.PlaceOrder(order);
            Assert.IsTrue(result);

            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void PlaceOrder_ShortPosition_PlacesSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, -1000, DateTime.UtcNow);

            var result = _brokerage.PlaceOrder(order);
            Assert.IsTrue(result);

            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        #endregion

        #region UpdateOrder Tests

        [Test]
        public void UpdateOrder_LimitPrice_UpdatesSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow, order.Id,
                new UpdateOrderFields { LimitPrice = 1.0600m });
            order.ApplyUpdateOrderRequest(updateRequest);
            var result = _brokerage.UpdateOrder(order);

            Assert.IsTrue(result);
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void UpdateOrder_NonExistentOrder_ReturnsFalse()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            var result = _brokerage.UpdateOrder(order);
            Assert.IsFalse(result);
        }

        #endregion

        #region CancelOrder Tests

        [Test]
        public void CancelOrder_PendingLimitOrder_CancelsSuccessfully()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            var result = _brokerage.CancelOrder(order);
            Assert.IsTrue(result);
        }

        [Test]
        public void CancelOrder_NonExistentOrder_ReturnsFalse()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            var result = _brokerage.CancelOrder(order);
            Assert.IsFalse(result);
        }

        #endregion
    }
}
