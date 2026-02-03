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
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Brokerages.IG.ToolBox;
using System;
using System.Linq;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials in config")]
    public class IGBrokerageDownloaderTests
    {
        private IGBrokerageDownloader _downloader;

        [SetUp]
        public void SetUp()
        {
            _downloader = new IGBrokerageDownloader();
        }

        [TearDown]
        public void TearDown()
        {
            _downloader?.Dispose();
        }

        [Test]
        public void DownloadsForexMinuteData()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, "ig");
            var startUtc = DateTime.UtcNow.AddDays(-7);
            var endUtc = DateTime.UtcNow.AddDays(-6);

            var parameters = new DataDownloaderGetParameters(
                symbol,
                Resolution.Minute,
                startUtc,
                endUtc,
                TickType.Quote
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert
            Assert.IsNotNull(data);
            Assert.IsNotEmpty(data, "Should return historical data");
            Assert.IsInstanceOf<QuoteBar>(data.First());

            Console.WriteLine($"Downloaded {data.Count} quote bars for {symbol}");

            foreach (var bar in data.Take(5))
            {
                var quoteBar = (QuoteBar)bar;
                Console.WriteLine($"{quoteBar.Time:yyyy-MM-dd HH:mm:ss} - Bid: {quoteBar.Bid?.Close}, Ask: {quoteBar.Ask?.Close}");

                Assert.Greater(quoteBar.Bid.Close, 0);
                Assert.Greater(quoteBar.Ask.Close, 0);
            }
        }

        [Test]
        public void DownloadsForexDailyData()
        {
            // Arrange
            var symbol = Symbol.Create("GBPUSD", SecurityType.Forex, "ig");
            var startUtc = DateTime.UtcNow.AddDays(-30);
            var endUtc = DateTime.UtcNow.AddDays(-1);

            var parameters = new DataDownloaderGetParameters(
                symbol,
                Resolution.Daily,
                startUtc,
                endUtc,
                TickType.Quote
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert
            Assert.IsNotNull(data);
            Assert.IsNotEmpty(data, "Should return historical data");
            Assert.IsInstanceOf<QuoteBar>(data.First());

            Console.WriteLine($"Downloaded {data.Count} daily bars for {symbol}");

            // Verify data is ordered by time
            var times = data.Select(d => d.Time).ToList();
            Assert.That(times, Is.Ordered);

            // Verify no duplicate timestamps
            Assert.AreEqual(times.Count, times.Distinct().Count(), "Should not have duplicate timestamps");
        }

        [Test]
        public void DownloadsIndexHourlyData()
        {
            // Arrange
            var symbol = Symbol.Create("SPX", SecurityType.Index, "ig");
            var startUtc = DateTime.UtcNow.AddDays(-3);
            var endUtc = DateTime.UtcNow.AddDays(-2);

            var parameters = new DataDownloaderGetParameters(
                symbol,
                Resolution.Hour,
                startUtc,
                endUtc,
                TickType.Trade
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert
            Assert.IsNotNull(data);
            Assert.IsNotEmpty(data, "Should return historical data");
            Assert.IsInstanceOf<TradeBar>(data.First());

            Console.WriteLine($"Downloaded {data.Count} hourly bars for {symbol}");

            foreach (var bar in data.Take(5))
            {
                var tradeBar = (TradeBar)bar;
                Console.WriteLine($"{tradeBar.Time:yyyy-MM-dd HH:mm:ss} - O:{tradeBar.Open} H:{tradeBar.High} L:{tradeBar.Low} C:{tradeBar.Close} V:{tradeBar.Volume}");

                Assert.Greater(tradeBar.Close, 0);
                Assert.GreaterOrEqual(tradeBar.High, tradeBar.Low);
            }
        }

        [Test]
        public void DownloadsCryptoData()
        {
            // Arrange
            var symbol = Symbol.Create("BTCUSD", SecurityType.Crypto, "ig");
            var startUtc = DateTime.UtcNow.AddDays(-2);
            var endUtc = DateTime.UtcNow.AddDays(-1);

            var parameters = new DataDownloaderGetParameters(
                symbol,
                Resolution.Minute,
                startUtc,
                endUtc,
                TickType.Trade
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert
            Assert.IsNotNull(data);
            if (data.Any())
            {
                Assert.IsInstanceOf<TradeBar>(data.First());
                Console.WriteLine($"Downloaded {data.Count} minute bars for {symbol}");
            }
            else
            {
                Assert.Warn("No crypto data available - this may be expected if IG doesn't support this crypto pair");
            }
        }

        [Test]
        public void HandlesInvalidDateRange()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, "ig");
            var startUtc = DateTime.UtcNow;
            var endUtc = DateTime.UtcNow.AddDays(-1); // End before start

            var parameters = new DataDownloaderGetParameters(
                symbol,
                Resolution.Minute,
                startUtc,
                endUtc,
                TickType.Quote
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert - should return empty collection, not throw exception
            Assert.IsNotNull(data);
            Assert.IsEmpty(data);
        }

        [Test]
        public void HandlesNullSymbol()
        {
            // Arrange
            var startUtc = DateTime.UtcNow.AddDays(-1);
            var endUtc = DateTime.UtcNow;

            var parameters = new DataDownloaderGetParameters(
                null,
                Resolution.Minute,
                startUtc,
                endUtc,
                TickType.Quote
            );

            // Act
            var data = _downloader.Get(parameters).ToList();

            // Assert - should return empty collection, not throw exception
            Assert.IsNotNull(data);
            Assert.IsEmpty(data);
        }

        [Test]
        public void DisposesCorrectly()
        {
            // Arrange
            var downloader = new IGBrokerageDownloader();

            // Act & Assert - should not throw exception
            Assert.DoesNotThrow(() => downloader.Dispose());
        }

        [Test]
        public void DownloadsMultipleResolutions()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, "ig");
            var startUtc = DateTime.UtcNow.AddDays(-3);
            var endUtc = DateTime.UtcNow.AddDays(-2);

            var resolutions = new[] { Resolution.Minute, Resolution.Hour };

            // Act & Assert
            foreach (var resolution in resolutions)
            {
                var parameters = new DataDownloaderGetParameters(
                    symbol,
                    resolution,
                    startUtc,
                    endUtc,
                    TickType.Quote
                );

                var data = _downloader.Get(parameters).ToList();

                Assert.IsNotNull(data, $"Should return data for {resolution}");
                Console.WriteLine($"{resolution}: Downloaded {data.Count} bars");
            }
        }
    }
}
