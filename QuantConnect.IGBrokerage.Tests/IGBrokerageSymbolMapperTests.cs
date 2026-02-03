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

        [Test]
        public void ReturnsCorrectBrokerageSymbol_MinorForexPair()
        {
            // Test EUR cross (minor pair)
            var symbol = Symbol.Create("EURGBP", SecurityType.Forex, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.EURGBP.MINI.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_ExoticForexPair()
        {
            // Test exotic pair
            var symbol = Symbol.Create("USDZAR", SecurityType.Forex, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.USDZAR.MINI.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_AsianIndex()
        {
            // Test Nikkei 225
            var symbol = Symbol.Create("N225", SecurityType.Index, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("IX.D.NIKKEI.DAILY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_EuropeanIndex()
        {
            // Test DAX
            var symbol = Symbol.Create("DAX", SecurityType.Index, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("IX.D.DAX.DAILY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Commodity_Gold()
        {
            // Test Gold
            var symbol = Symbol.Create("XAUUSD", SecurityType.Cfd, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.USCGC.TODAY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Commodity_Oil()
        {
            // Test Crude Oil
            var symbol = Symbol.Create("CL", SecurityType.Cfd, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CC.D.CL.USS.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Commodity_NaturalGas()
        {
            // Test Natural Gas
            var symbol = Symbol.Create("NG", SecurityType.Cfd, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CC.D.NG.USS.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Crypto_Ethereum()
        {
            // Test Ethereum
            var symbol = Symbol.Create("ETHUSD", SecurityType.Crypto, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.ETHUSD.CFD.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Crypto_Solana()
        {
            // Test Solana
            var symbol = Symbol.Create("SOLUSD", SecurityType.Crypto, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("CS.D.SOLANA.CFD.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Equity_Apple()
        {
            // Test Apple stock
            var symbol = Symbol.Create("AAPL", SecurityType.Equity, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("IX.D.APPLE.DAILY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectBrokerageSymbol_Equity_Tesla()
        {
            // Test Tesla stock
            var symbol = Symbol.Create("TSLA", SecurityType.Equity, Market.IG);
            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(symbol);

            Assert.AreEqual("IX.D.TESLA.DAILY.IP", brokerageSymbol);
        }

        [Test]
        public void ReturnsCorrectLeanSymbol_Commodity_Silver()
        {
            // Test Silver mapping
            var leanSymbol = _symbolMapper.GetLeanSymbol("CS.D.USCSI.TODAY.IP", SecurityType.Cfd, Market.IG);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("XAGUSD", leanSymbol.Value);
            Assert.AreEqual(SecurityType.Cfd, leanSymbol.SecurityType);
            Assert.AreEqual(Market.IG, leanSymbol.ID.Market);
        }

        [Test]
        public void ReturnsCorrectLeanSymbol_Equity_Microsoft()
        {
            // Test Microsoft mapping
            var leanSymbol = _symbolMapper.GetLeanSymbol("IX.D.MICROSOFT.DAILY.IP", SecurityType.Equity, Market.IG);

            Assert.IsNotNull(leanSymbol);
            Assert.AreEqual("MSFT", leanSymbol.Value);
            Assert.AreEqual(SecurityType.Equity, leanSymbol.SecurityType);
            Assert.AreEqual(Market.IG, leanSymbol.ID.Market);
        }

        [Test]
        public void AddMapping_CustomSymbol_WorksCorrectly()
        {
            // Test adding a custom mapping
            _symbolMapper.AddMapping("CUSTOMFX", "CS.D.CUSTOM.MINI.IP", SecurityType.Forex);

            var brokerageSymbol = _symbolMapper.GetBrokerageSymbol(Symbol.Create("CUSTOMFX", SecurityType.Forex, Market.IG));
            Assert.AreEqual("CS.D.CUSTOM.MINI.IP", brokerageSymbol);

            var leanSymbol = _symbolMapper.GetLeanSymbol("CS.D.CUSTOM.MINI.IP", SecurityType.Forex, Market.IG);
            Assert.AreEqual("CUSTOMFX", leanSymbol.Value);
        }

        [Test]
        public void SupportsComprehensiveForexPairs_Count()
        {
            // Verify we have a significant number of forex pairs mapped
            var testPairs = new[] { "EURUSD", "GBPJPY", "AUDJPY", "NZDCHF", "EURNOK", "USDMXN", "USDZAR" };

            foreach (var pair in testPairs)
            {
                var symbol = Symbol.Create(pair, SecurityType.Forex, Market.IG);
                var epic = _symbolMapper.GetBrokerageSymbol(symbol);
                Assert.IsNotNull(epic, $"Forex pair {pair} should be mapped");
                Assert.IsTrue(epic.Contains("MINI.IP"), $"Forex pair {pair} should use MINI contract");
            }
        }

        [Test]
        public void SupportsGlobalIndices_Count()
        {
            // Verify we have global indices mapped
            var testIndices = new[] { "SPX", "DAX", "FTSE", "N225", "HSI", "AUS200" };

            foreach (var index in testIndices)
            {
                var symbol = Symbol.Create(index, SecurityType.Index, Market.IG);
                var epic = _symbolMapper.GetBrokerageSymbol(symbol);
                Assert.IsNotNull(epic, $"Index {index} should be mapped");
                Assert.IsTrue(epic.Contains("DAILY.IP"), $"Index {index} should use DAILY contract");
            }
        }
    }
}