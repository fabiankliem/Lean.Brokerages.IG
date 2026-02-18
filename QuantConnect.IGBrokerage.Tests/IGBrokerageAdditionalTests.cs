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
using System.Reflection;
using NUnit.Framework;
using QuantConnect.Util;
using QuantConnect.Interfaces;
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

        #region ParseStopLossAndTakeProfit Tests

        [Test]
        public void ParseStopLossAndTakeProfit_ValidTag_ParsesCorrectly()
        {
            var parseMethod = GetStaticMethod("ParseStopLossAndTakeProfit");
            var parameters = new object[] { "SL:1.1000;TP:1.2000", null, null };

            parseMethod.Invoke(null, parameters);

            Assert.AreEqual(1.1000m, (decimal?)parameters[1]);
            Assert.AreEqual(1.2000m, (decimal?)parameters[2]);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_OnlyStopLoss_ParsesCorrectly()
        {
            var parseMethod = GetStaticMethod("ParseStopLossAndTakeProfit");
            var parameters = new object[] { "SL:1.0500", null, null };

            parseMethod.Invoke(null, parameters);

            Assert.AreEqual(1.0500m, (decimal?)parameters[1]);
            Assert.IsNull((decimal?)parameters[2]);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_OnlyTakeProfit_ParsesCorrectly()
        {
            var parseMethod = GetStaticMethod("ParseStopLossAndTakeProfit");
            var parameters = new object[] { "TP:1.3000", null, null };

            parseMethod.Invoke(null, parameters);

            Assert.IsNull((decimal?)parameters[1]);
            Assert.AreEqual(1.3000m, (decimal?)parameters[2]);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_InvalidTag_ReturnsNull()
        {
            var parseMethod = GetStaticMethod("ParseStopLossAndTakeProfit");
            var parameters = new object[] { "Invalid tag format", null, null };

            parseMethod.Invoke(null, parameters);

            Assert.IsNull((decimal?)parameters[1]);
            Assert.IsNull((decimal?)parameters[2]);
        }

        [Test]
        public void ParseStopLossAndTakeProfit_EmptyTag_ReturnsNull()
        {
            var parseMethod = GetStaticMethod("ParseStopLossAndTakeProfit");
            var parameters = new object[] { "", null, null };

            parseMethod.Invoke(null, parameters);

            Assert.IsNull((decimal?)parameters[1]);
            Assert.IsNull((decimal?)parameters[2]);
        }

        #endregion

        #region CanSubscribe Tests

        [Test]
        public void CanSubscribe_SupportedForexSymbol_ReturnsTrue()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, IGSymbolMapper.MarketName);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsTrue(result, "Should accept supported forex symbol");
        }

        [Test]
        public void CanSubscribe_SupportedIndexSymbol_ReturnsTrue()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("SPX", SecurityType.Index, IGSymbolMapper.MarketName);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsTrue(result, "Should accept supported index symbol");
        }

        [Test]
        public void CanSubscribe_SupportedCryptoSymbol_ReturnsTrue()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("BTCUSD", SecurityType.Crypto, IGSymbolMapper.MarketName);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsTrue(result, "Should accept supported crypto symbol");
        }

        [Test]
        public void CanSubscribe_SupportedCfdSymbol_ReturnsTrue()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("XAUUSD", SecurityType.Cfd, IGSymbolMapper.MarketName);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsTrue(result, "Should accept supported CFD symbol");
        }

        [Test]
        public void CanSubscribe_SupportedEquitySymbol_ReturnsTrue()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("AAPL", SecurityType.Equity, IGSymbolMapper.MarketName);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsTrue(result, "Should accept supported equity symbol");
        }

        [Test]
        public void CanSubscribe_UnsupportedFutureSecurityType_ReturnsFalse()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");
            var symbol = Symbol.Create("ES", SecurityType.Future, Market.CME);

            var result = (bool)method.Invoke(brokerage, new object[] { symbol });

            Assert.IsFalse(result, "Should reject unsupported Future security type");
        }

        [Test]
        public void CanSubscribe_MultipleSecurityTypes_ValidatesCorrectly()
        {
            var brokerage = new IGBrokerage();
            var method = GetInstanceMethod(brokerage, "CanSubscribe");

            var testCases = new[]
            {
                (Symbol.Create("EURUSD", SecurityType.Forex, IGSymbolMapper.MarketName), true),
                (Symbol.Create("SPX", SecurityType.Index, IGSymbolMapper.MarketName), true),
                (Symbol.Create("BTCUSD", SecurityType.Crypto, IGSymbolMapper.MarketName), true),
                (Symbol.Create("XAUUSD", SecurityType.Cfd, IGSymbolMapper.MarketName), true),
                (Symbol.Create("AAPL", SecurityType.Equity, IGSymbolMapper.MarketName), true),
                (Symbol.Create("ES", SecurityType.Future, Market.CME), false),
            };

            foreach (var (symbol, expected) in testCases)
            {
                var result = (bool)method.Invoke(brokerage, new object[] { symbol });
                Assert.AreEqual(expected, result, $"Validation failed for {symbol.Value} ({symbol.SecurityType})");
            }
        }

        #endregion

        #region Helpers

        private static MethodInfo GetStaticMethod(string methodName)
        {
            var method = typeof(IGBrokerage).GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Static);

            if (method == null)
            {
                throw new InvalidOperationException($"Static method '{methodName}' not found on IGBrokerage");
            }

            return method;
        }

        private static MethodInfo GetInstanceMethod(object obj, string methodName)
        {
            var method = obj.GetType().GetMethod(methodName,
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (method == null)
            {
                throw new InvalidOperationException($"Instance method '{methodName}' not found on {obj.GetType().Name}");
            }

            return method;
        }

        #endregion
    }
}
