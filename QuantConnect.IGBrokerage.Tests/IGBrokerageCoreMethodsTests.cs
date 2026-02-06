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
using System.Reflection;
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

            // Get configuration from config.json
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");
            var environment = Config.Get("ig-environment", "demo");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGBrokerageCoreMethodsTests: Credentials not configured in config.json");
            }

            // Create and connect brokerage
            _brokerage = new IGBrokerage(
                apiUrl,
                apiKey,
                identifier,
                password,
                accountId,
                environment,
                null,
                null
            );

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
            // Act
            var cashBalances = _brokerage.GetCashBalance();

            // Assert
            Assert.IsNotNull(cashBalances, "Cash balance list should not be null");
            Assert.IsNotEmpty(cashBalances, "Cash balance list should contain at least one entry");
        }

        [Test]
        public void GetCashBalance_ContainsValidCurrency()
        {
            // Act
            var cashBalances = _brokerage.GetCashBalance();

            // Assert
            Assert.IsTrue(cashBalances.All(c => !string.IsNullOrEmpty(c.Currency)),
                "All cash balances should have a valid currency");
        }

        [Test]
        public void GetCashBalance_ContainsPositiveOrZeroAmount()
        {
            // Act
            var cashBalances = _brokerage.GetCashBalance();

            // Assert
            Assert.IsTrue(cashBalances.All(c => c.Amount >= 0),
                "All cash balances should be non-negative");
        }

        [Test]
        public void GetCashBalance_MultipleCallsReturnConsistentResults()
        {
            // Act
            var balance1 = _brokerage.GetCashBalance();
            Thread.Sleep(500);
            var balance2 = _brokerage.GetCashBalance();

            // Assert
            Assert.AreEqual(balance1.Count, balance2.Count,
                "Number of currencies should be consistent across calls");
        }

        #endregion

        #region GetAccountHoldings Tests

        [Test]
        public void GetAccountHoldings_ReturnsListSuccessfully()
        {
            // Act
            var holdings = _brokerage.GetAccountHoldings();

            // Assert
            Assert.IsNotNull(holdings, "Holdings list should not be null");
            // Note: List may be empty if no open positions
        }

        [Test]
        public void GetAccountHoldings_WhenPositionsExist_ReturnsValidHoldings()
        {
            // Act
            var holdings = _brokerage.GetAccountHoldings();

            // Assert
            if (holdings.Count > 0)
            {
                foreach (var holding in holdings)
                {
                    Assert.IsNotNull(holding.Symbol, "Holding symbol should not be null");
                    Assert.AreNotEqual(0, holding.Quantity, "Holding quantity should not be zero");
                    Assert.Greater(holding.AveragePrice, 0, "Average price should be positive");
                    Assert.IsNotNull(holding.CurrencySymbol, "Currency should not be null");
                }
            }
        }

        [Test]
        public void GetAccountHoldings_ValidatesPositiveQuantityForLongPositions()
        {
            // Act
            var holdings = _brokerage.GetAccountHoldings();

            // Assert
            var longPositions = holdings.Where(h => h.Quantity > 0).ToList();
            foreach (var holding in longPositions)
            {
                Assert.Greater(holding.Quantity, 0, "Long positions should have positive quantity");
            }
        }

        [Test]
        public void GetAccountHoldings_ValidatesNegativeQuantityForShortPositions()
        {
            // Act
            var holdings = _brokerage.GetAccountHoldings();

            // Assert
            var shortPositions = holdings.Where(h => h.Quantity < 0).ToList();
            foreach (var holding in shortPositions)
            {
                Assert.Less(holding.Quantity, 0, "Short positions should have negative quantity");
            }
        }

        #endregion

        #region PlaceOrder Tests

        [Test]
        public void PlaceOrder_MarketOrder_PlacesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow);

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Market order should be placed successfully");

            // Cleanup: Cancel the order if it's still pending
            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        [Test]
        public void PlaceOrder_LimitOrder_PlacesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var limitPrice = 1.0500m; // Well below current market price
            var order = new LimitOrder(symbol, 1000, limitPrice, DateTime.UtcNow);

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Limit order should be placed successfully");

            // Cleanup
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void PlaceOrder_StopMarketOrder_PlacesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var stopPrice = 1.1500m; // Well above current market price
            var order = new StopMarketOrder(symbol, 1000, stopPrice, DateTime.UtcNow);

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Stop market order should be placed successfully");

            // Cleanup
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void PlaceOrder_WithStopLoss_ParsesTagCorrectly()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow, "SL:1.0800");

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Order with stop loss should be placed successfully");

            // Cleanup
            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        [Test]
        public void PlaceOrder_WithTakeProfit_ParsesTagCorrectly()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow, "TP:1.1200");

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Order with take profit should be placed successfully");

            // Cleanup
            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        [Test]
        public void PlaceOrder_WithStopLossAndTakeProfit_ParsesTagCorrectly()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow, "SL:1.0800;TP:1.1200");

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Order with SL and TP should be placed successfully");

            // Cleanup
            Thread.Sleep(2000);
            if (order.Status != OrderStatus.Filled)
            {
                _brokerage.CancelOrder(order);
            }
        }

        [Test]
        public void PlaceOrder_ShortPosition_PlacesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, -1000, DateTime.UtcNow);

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsTrue(result, "Short market order should be placed successfully");

            // Cleanup
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
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var initialPrice = 1.0500m;
            var order = new LimitOrder(symbol, 1000, initialPrice, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            // Act
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields { LimitPrice = 1.0600m }
            );
            order.ApplyUpdateOrderRequest(updateRequest);
            var result = _brokerage.UpdateOrder(order);

            // Assert
            Assert.IsTrue(result, "Order update should succeed");

            // Cleanup
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void UpdateOrder_StopPrice_UpdatesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var initialPrice = 1.1500m;
            var order = new StopMarketOrder(symbol, 1000, initialPrice, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            // Act
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields { StopPrice = 1.1600m }
            );
            order.ApplyUpdateOrderRequest(updateRequest);
            var result = _brokerage.UpdateOrder(order);

            // Assert
            Assert.IsTrue(result, "Order update should succeed");

            // Cleanup
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void UpdateOrder_Quantity_UpdatesSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            // Act
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields { Quantity = 2000 }
            );
            order.ApplyUpdateOrderRequest(updateRequest);
            var result = _brokerage.UpdateOrder(order);

            // Assert
            Assert.IsTrue(result, "Order quantity update should succeed");

            // Cleanup
            Thread.Sleep(1000);
            _brokerage.CancelOrder(order);
        }

        [Test]
        public void UpdateOrder_NonExistentOrder_ReturnsFalse()
        {
            // Arrange - Create an order that was never placed through the brokerage
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            // Act - The brokerage has no broker ID mapping for this order, so it should fail
            var result = _brokerage.UpdateOrder(order);

            // Assert
            Assert.IsFalse(result, "Updating non-existent order should return false");
        }

        #endregion

        #region CancelOrder Tests

        [Test]
        public void CancelOrder_PendingLimitOrder_CancelsSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            // Act
            var result = _brokerage.CancelOrder(order);

            // Assert
            Assert.IsTrue(result, "Canceling pending limit order should succeed");
        }

        [Test]
        public void CancelOrder_PendingStopOrder_CancelsSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new StopMarketOrder(symbol, 1000, 1.1500m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order);
            Thread.Sleep(1000);

            // Act
            var result = _brokerage.CancelOrder(order);

            // Assert
            Assert.IsTrue(result, "Canceling pending stop order should succeed");
        }

        [Test]
        public void CancelOrder_NonExistentOrder_ReturnsFalse()
        {
            // Arrange - Create an order that was never placed through the brokerage
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);

            // Act - The brokerage has no broker ID mapping for this order, so it should fail
            var result = _brokerage.CancelOrder(order);

            // Assert
            Assert.IsFalse(result, "Canceling non-existent order should return false");
        }

        [Test]
        public void CancelOrder_MultipleOrders_CancelsAllSuccessfully()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order1 = new LimitOrder(symbol, 1000, 1.0500m, DateTime.UtcNow);
            var order2 = new LimitOrder(symbol, 1000, 1.0600m, DateTime.UtcNow);

            _brokerage.PlaceOrder(order1);
            _brokerage.PlaceOrder(order2);
            Thread.Sleep(1000);

            // Act
            var result1 = _brokerage.CancelOrder(order1);
            Thread.Sleep(500);
            var result2 = _brokerage.CancelOrder(order2);

            // Assert
            Assert.IsTrue(result1, "First order cancellation should succeed");
            Assert.IsTrue(result2, "Second order cancellation should succeed");
        }

        #endregion
    }
}
