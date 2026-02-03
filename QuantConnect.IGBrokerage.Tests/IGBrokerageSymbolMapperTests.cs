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

using NUnit.Framework;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture]
    public class IGBrokerageSymbolMapperTests
    {
        private IGSymbolMapper _symbolMapper;

        [SetUp]
        public void Setup()
        {
            _symbolMapper = new IGSymbolMapper();
        }

        [Test]
        public void ReturnsCorrectLeanSymbol_Forex()
        {
            // Test EURUSD mapping
            var leanSymbol = _symbolMapper.GetLeanSymbol("CS.D.EURUSD.MINI.IP", SecurityType.Forex, Market.IG);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("EURUSD", leanSymbol.Value);
            Assert.AreEqual(SecurityType.Forex, leanSymbol.SecurityType);
            Assert.AreEqual(Market.IG, leanSymbol.ID.Market);
        }

        [Test]
        public void ReturnsCorrectLeanSymbol_Index()
        {
            // Test FTSE 100 mapping
            var leanSymbol = _symbolMapper.GetLeanSymbol("IX.D.FTSE.DAILY.IP", SecurityType.Index, Market.IG);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("FTSE", leanSymbol.Value);
            Assert.AreEqual(SecurityType.Index, leanSymbol.SecurityType);
            Assert.AreEqual(Market.IG, leanSymbol.ID.Market);
        }

        [Test]
        public void ReturnsCorrectLeanSymbol_Crypto()
        {
            // Test Bitcoin mapping
            var leanSymbol = _symbolMapper.GetLeanSymbol("CS.D.BITCOIN.CFD.IP", SecurityType.Crypto, Market.IG);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("BTCUSD", leanSymbol.Value);
            Assert.AreEqual(SecurityType.Crypto, leanSymbol.SecurityType);
            Assert.AreEqual(Market.IG, leanSymbol.ID.Market);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Forex()
        {
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.EURUSD.MINI.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Index()
        {
            var symbol = Symbol.Create("FTSE", SecurityType.Index, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("IX.D.FTSE.DAILY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Crypto()
        {
            var symbol = Symbol.Create("BTCUSD", SecurityType.Crypto, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.BITCOIN.CFD.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsNull_ForUnknownEpic()
        {
            var leanSymbol = _symbolMapper.GetLeanSymbol("UNKNOWN.EPIC.CODE", SecurityType.Forex, Market.IG);

            Assert.IsNull(leanSymbol);
        }

        [Test]
        public void ReturnsNull_ForUnknownSymbol()
        {
            var symbol = Symbol.Create("UNKNOWNSYMBOL", SecurityType.Forex, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.IsNull(brokerageSymbol);
        }

        [Test]
        public void RoundTrip_Forex()
        {
            // Test that converting LEAN -> EPIC -> LEAN gives us the same symbol
            var originalSymbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var epic = _symbolMapper.GetBrokerageSymbol(originalSymbol);
            var roundTripSymbol = _symbolMapper.GetLeanSymbol(epic, SecurityType.Forex, Market.IG);

            Assert.AreEqual(originalSymbol.Value, roundTripSymbol.Value);
            Assert.AreEqual(originalSymbol.SecurityType, roundTripSymbol.SecurityType);
        }

        [Test]
        public void RoundTrip_Index()
        {
            // Test that converting LEAN -> EPIC -> LEAN gives us the same symbol
            var originalSymbol = Symbol.Create("SPX500", SecurityType.Index, Market.IG);
            var epic = _symbolMapper.GetBrokerageSymbol(originalSymbol);
            var roundTripSymbol = _symbolMapper.GetLeanSymbol(epic, SecurityType.Index, Market.IG);

            Assert.AreEqual(originalSymbol.Value, roundTripSymbol.Value);
            Assert.AreEqual(originalSymbol.SecurityType, roundTripSymbol.SecurityType);
        }
    }
}