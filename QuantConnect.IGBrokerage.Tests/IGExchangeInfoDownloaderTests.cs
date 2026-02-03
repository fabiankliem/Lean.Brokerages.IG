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
using QuantConnect.ToolBox;
using QuantConnect.Brokerages.IG.ToolBox;
using System;
using System.Linq;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials in config")]
    public class IGExchangeInfoDownloaderTests
    {
        [Test]
        public void GetsExchangeInfo()
        {
            var downloader = new IGExchangeInfoDownloader();
            var tickers = downloader.Get().ToList();

            Assert.IsTrue(tickers.Any());
            Assert.AreEqual("ig", downloader.Market);

            Console.WriteLine($"Downloaded {tickers.Count} instruments");

            foreach (var tickerLine in tickers.Take(10)) // Show first 10 for verification
            {
                Console.WriteLine(tickerLine);

                Assert.IsTrue(tickerLine.StartsWith("ig,", StringComparison.Ordinal));
                var data = tickerLine.Split(',');
                Assert.AreEqual(12, data.Length, $"Expected 12 columns, got {data.Length}");

                var epic = data[1]; // EPIC code
                var securityType = data[2]; // SecurityType
                var description = data[3]; // Description
                var quoteCurrency = data[4]; // Quote currency

                Assert.IsNotEmpty(epic, "EPIC should not be empty");
                Assert.IsNotEmpty(securityType, "SecurityType should not be empty");
                Assert.IsNotEmpty(quoteCurrency, "Quote currency should not be empty");

                // Verify security type is valid
                Assert.IsTrue(new[] { "forex", "index", "cfd", "crypto", "equity", "option" }.Contains(securityType.ToLowerInvariant()),
                    $"Invalid security type: {securityType}");
            }

            Console.WriteLine($"\nTotal instruments downloaded: {tickers.Count}");

            // Verify we have instruments from different asset classes
            var byType = tickers.GroupBy(t => t.Split(',')[2]).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine("\nBreakdown by asset class:");
            foreach (var kvp in byType)
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} instruments");
            }
        }

        [Test]
        public void MarketPropertyReturnsIG()
        {
            var downloader = new IGExchangeInfoDownloader();
            Assert.AreEqual("ig", downloader.Market);
        }
    }
}