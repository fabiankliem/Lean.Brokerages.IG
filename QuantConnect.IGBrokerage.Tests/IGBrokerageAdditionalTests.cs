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
using System.Reflection;
using NUnit.Framework;
using QuantConnect.Util;
using QuantConnect.Interfaces;
using QuantConnect.Orders;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture]
    public class IGBrokerageAdditionalTests
    {
        [Test]
        public void ParameterlessConstructorComposerUsage()
        {
            var brokerage = Composer.Instance.GetExportedValueByTypeName<IDataQueueHandler>("IGBrokerage");
            Assert.IsNotNull(brokerage);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_ValidTag_ParsesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var parseMethod = GetPrivateMethod(brokerage, "ParseStopLossAndTakeProfit");
            var tag = "SL:1.1000;TP:1.2000";
            var parameters = new object[] { tag, null, null };

            // Act
            parseMethod.Invoke(brokerage, parameters);
            var stopLossPrice = (decimal?)parameters[1];
            var takeProfitPrice = (decimal?)parameters[2];

            // Assert
            Assert.IsNotNull(stopLossPrice);
            Assert.IsNotNull(takeProfitPrice);
            Assert.AreEqual(1.1000m, stopLossPrice.Value);
            Assert.AreEqual(1.2000m, takeProfitPrice.Value);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_OnlyStopLoss_ParsesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var parseMethod = GetPrivateMethod(brokerage, "ParseStopLossAndTakeProfit");
            var tag = "SL:1.0500";
            var parameters = new object[] { tag, null, null };

            // Act
            parseMethod.Invoke(brokerage, parameters);
            var stopLossPrice = (decimal?)parameters[1];
            var takeProfitPrice = (decimal?)parameters[2];

            // Assert
            Assert.IsNotNull(stopLossPrice);
            Assert.IsNull(takeProfitPrice);
            Assert.AreEqual(1.0500m, stopLossPrice.Value);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_OnlyTakeProfit_ParsesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var parseMethod = GetPrivateMethod(brokerage, "ParseStopLossAndTakeProfit");
            var tag = "TP:1.3000";
            var parameters = new object[] { tag, null, null };

            // Act
            parseMethod.Invoke(brokerage, parameters);
            var stopLossPrice = (decimal?)parameters[1];
            var takeProfitPrice = (decimal?)parameters[2];

            // Assert
            Assert.IsNull(stopLossPrice);
            Assert.IsNotNull(takeProfitPrice);
            Assert.AreEqual(1.3000m, takeProfitPrice.Value);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_InvalidTag_ReturnsNull()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var parseMethod = GetPrivateMethod(brokerage, "ParseStopLossAndTakeProfit");
            var tag = "Invalid tag format";
            var parameters = new object[] { tag, null, null };

            // Act
            parseMethod.Invoke(brokerage, parameters);
            var stopLossPrice = (decimal?)parameters[1];
            var takeProfitPrice = (decimal?)parameters[2];

            // Assert
            Assert.IsNull(stopLossPrice);
            Assert.IsNull(takeProfitPrice);
        }

        [Test]
        public void CalculatePriceDistance_BuyOrder_StopLossBelow_CalculatesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var calculateMethod = GetPrivateMethod(brokerage, "CalculatePriceDistance");
            var entryPrice = 1.2000m;
            var stopLossPrice = 1.1900m;  // 100 points below
            var direction = OrderDirection.Buy;

            // Act
            var distance = (decimal)calculateMethod.Invoke(brokerage, new object[] { entryPrice, stopLossPrice, direction });

            // Assert
            Assert.AreEqual(0.0100m, distance);  // 100 points
        }

        [Test]
        public void CalculatePriceDistance_BuyOrder_TakeProfitAbove_CalculatesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var calculateMethod = GetPrivateMethod(brokerage, "CalculatePriceDistance");
            var entryPrice = 1.2000m;
            var takeProfitPrice = 1.2150m;  // 150 points above
            var direction = OrderDirection.Buy;

            // Act - For take profit, we pass Sell direction (opposite)
            var distance = (decimal)calculateMethod.Invoke(brokerage, new object[] { entryPrice, takeProfitPrice, OrderDirection.Sell });

            // Assert
            Assert.AreEqual(0.0150m, distance);  // 150 points
        }

        [Test]
        public void CalculatePriceDistance_SellOrder_StopLossAbove_CalculatesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var calculateMethod = GetPrivateMethod(brokerage, "CalculatePriceDistance");
            var entryPrice = 1.2000m;
            var stopLossPrice = 1.2100m;  // 100 points above
            var direction = OrderDirection.Sell;

            // Act
            var distance = (decimal)calculateMethod.Invoke(brokerage, new object[] { entryPrice, stopLossPrice, direction });

            // Assert
            Assert.AreEqual(0.0100m, distance);  // 100 points
        }

        [Test]
        public void CalculatePriceDistance_SellOrder_TakeProfitBelow_CalculatesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var calculateMethod = GetPrivateMethod(brokerage, "CalculatePriceDistance");
            var entryPrice = 1.2000m;
            var takeProfitPrice = 1.1850m;  // 150 points below
            var direction = OrderDirection.Sell;

            // Act - For take profit, we pass Buy direction (opposite)
            var distance = (decimal)calculateMethod.Invoke(brokerage, new object[] { entryPrice, takeProfitPrice, OrderDirection.Buy });

            // Assert
            Assert.AreEqual(0.0150m, distance);  // 150 points
        }

        [Test]
        public void ValidateSubscription_SupportedSymbol_ReturnsTrue()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);

            // Act
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Forex, Resolution.Minute, TickType.Quote });

            // Assert
            Assert.IsTrue(result, "Should validate supported forex symbol");
        }

        [Test]
        public void ValidateSubscription_UnsupportedSecurityType_ReturnsFalse()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("ES", SecurityType.Future, Market.IG);

            // Act
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Future, Resolution.Minute, TickType.Trade });

            // Assert
            Assert.IsFalse(result, "Should reject unsupported Future security type");
        }

        [Test]
        public void ValidateSubscription_UnmappedSymbol_ReturnsFalse()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("INVALIDXYZ", SecurityType.Forex, Market.IG);

            // Act
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Forex, Resolution.Minute, TickType.Trade });

            // Assert
            Assert.IsFalse(result, "Should reject unmapped symbol");
        }

        [Test]
        public void ValidateSubscription_TickResolution_ReturnsFalse()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);

            // Act
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Forex, Resolution.Tick, TickType.Quote });

            // Assert
            Assert.IsFalse(result, "Should reject Tick resolution");
        }

        [Test]
        public void ValidateSubscription_OpenInterestTickType_ReturnsFalse()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);

            // Act
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Forex, Resolution.Minute, TickType.OpenInterest });

            // Assert
            Assert.IsFalse(result, "Should reject OpenInterest tick type");
        }

        [Test]
        public void ValidateSubscription_IndexWithQuote_ReturnsTrue()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");
            var symbol = Symbol.Create("SPX", SecurityType.Index, Market.IG);

            // Act - Should warn but still return true
            var result = (bool)method.Invoke(brokerage,
                new object[] { symbol, SecurityType.Index, Resolution.Minute, TickType.Quote });

            // Assert
            Assert.IsTrue(result, "Should validate but warn about Index with Quote");
        }

        [Test]
        public void ValidateSubscription_MultipleSecurityTypes_ValidatesCorrectly()
        {
            // Arrange
            var brokerage = new IGBrokerage();
            var method = GetPrivateMethod(brokerage, "ValidateSubscription");

            var testCases = new[]
            {
                (Symbol.Create("EURUSD", SecurityType.Forex, Market.IG), SecurityType.Forex, true),
                (Symbol.Create("SPX", SecurityType.Index, Market.IG), SecurityType.Index, true),
                (Symbol.Create("BTCUSD", SecurityType.Crypto, Market.IG), SecurityType.Crypto, true),
                (Symbol.Create("XAUUSD", SecurityType.Cfd, Market.IG), SecurityType.Cfd, true),
                (Symbol.Create("AAPL", SecurityType.Equity, Market.IG), SecurityType.Equity, true),
            };

            // Act & Assert
            foreach (var (symbol, secType, expected) in testCases)
            {
                var result = (bool)method.Invoke(brokerage,
                    new object[] { symbol, secType, Resolution.Minute, TickType.Trade });
                Assert.AreEqual(expected, result, $"Validation failed for {symbol.Value} ({secType})");
            }
        }

        /// <summary>
        /// Helper method to access private methods via reflection for testing
        /// </summary>
        private MethodInfo GetPrivateMethod(object obj, string methodName)
        {
            var method = obj.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new InvalidOperationException($"Method '{methodName}' not found");
            }

            return method;
        }
    }
}