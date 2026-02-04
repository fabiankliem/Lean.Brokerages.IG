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
using NUnit.Framework;
using QuantConnect.Orders;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture]
    public class IGOrderFeeCalculatorTests
    {
        #region Forex Tests (Zero Commission)

        [Test]
        public void CalculatesFee_ForexOrder_ReturnsZero()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                1000,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 1.1000m, 1000);

            // Assert
            Assert.AreEqual(0m, fee.Value.Amount);
            Assert.AreEqual("USD", fee.Value.Currency); // Zero fee uses USD
        }

        [Test]
        public void CalculatesFee_ForexLargeOrder_ReturnsZero()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG),
                100000,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 1.3000m, 100000);

            // Assert - Forex always zero regardless of size
            Assert.AreEqual(0m, fee.Value.Amount);
        }

        #endregion

        #region Index Tests (Commission-Based)

        [Test]
        public void CalculatesFee_SmallIndexOrder_ReturnsMinimumFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                1,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, 1);

            // Assert - 4000 * 0.001 = 4, but min is 10
            Assert.AreEqual(10m, fee.Value.Amount);
            Assert.AreEqual("GBP", fee.Value.Currency);
        }

        [Test]
        public void CalculatesFee_LargeIndexOrder_ReturnsPercentageFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                100,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, 100);

            // Assert - 400000 * 0.001 = 400
            Assert.AreEqual(400m, fee.Value.Amount);
            Assert.AreEqual("GBP", fee.Value.Currency);
        }

        [Test]
        public void CalculatesFee_IndexAtMinimumThreshold_ReturnsMinimumFee()
        {
            // Arrange - Calculate exact minimum threshold: 10 / 0.001 = 10000
            var order = new MarketOrder(
                Symbol.Create("FTSE", SecurityType.Index, Market.IG),
                1,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 10000m, 1);

            // Assert - 10000 * 0.001 = 10 (exactly at minimum)
            Assert.AreEqual(10m, fee.Value.Amount);
        }

        #endregion

        #region CFD Tests (Commission-Based)

        [Test]
        public void CalculatesFee_CfdOrder_AppliesCommission()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("GOLD", SecurityType.Cfd, Market.IG),
                10,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 1800m, 10);

            // Assert - 18000 * 0.001 = 18
            Assert.AreEqual(18m, fee.Value.Amount);
            Assert.AreEqual("GBP", fee.Value.Currency);
        }

        [Test]
        public void CalculatesFee_SmallCfdOrder_ReturnsMinimumFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("OIL", SecurityType.Cfd, Market.IG),
                1,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 80m, 1);

            // Assert - 80 * 0.001 = 0.08, min is 10
            Assert.AreEqual(10m, fee.Value.Amount);
        }

        #endregion

        #region Equity Tests (Commission-Based)

        [Test]
        public void CalculatesFee_EquityOrder_AppliesCommission()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("AAPL", SecurityType.Equity, Market.IG),
                50,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 150m, 50);

            // Assert - 7500 * 0.001 = 7.5, min is 10
            Assert.AreEqual(10m, fee.Value.Amount);
        }

        [Test]
        public void CalculatesFee_LargeEquityOrder_ReturnsPercentageFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("MSFT", SecurityType.Equity, Market.IG),
                500,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 300m, 500);

            // Assert - 150000 * 0.001 = 150
            Assert.AreEqual(150m, fee.Value.Amount);
        }

        #endregion

        #region Crypto Tests (Zero Commission)

        [Test]
        public void CalculatesFee_CryptoOrder_ReturnsZero()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("BTCUSD", SecurityType.Crypto, Market.IG),
                0.5m,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 50000m, 0.5m);

            // Assert - Crypto is spread-based
            Assert.AreEqual(0m, fee.Value.Amount);
        }

        #endregion

        #region Order Event Tests

        [Test]
        public void CalculatesFee_FromOrderEvent_ReturnsCorrectFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                100,
                DateTime.UtcNow
            );

            var orderEvent = new OrderEvent(order, DateTime.UtcNow, Orders.Fees.OrderFee.Zero)
            {
                Status = OrderStatus.Filled,
                FillPrice = 4000m,
                FillQuantity = 100
            };

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(orderEvent);

            // Assert
            Assert.AreEqual(400m, fee.Value.Amount);
            Assert.AreEqual("GBP", fee.Value.Currency);
        }

        [Test]
        public void CalculatesFee_PartiallyFilledOrder_CalculatesPartialFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                100,
                DateTime.UtcNow
            );

            var orderEvent = new OrderEvent(order, DateTime.UtcNow, Orders.Fees.OrderFee.Zero)
            {
                Status = OrderStatus.PartiallyFilled,
                FillPrice = 4000m,
                FillQuantity = 50 // Only half filled
            };

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(orderEvent);

            // Assert - Half the full quantity
            Assert.AreEqual(200m, fee.Value.Amount);
        }

        [Test]
        public void CalculatesFee_SubmittedOrder_ReturnsZero()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                100,
                DateTime.UtcNow
            );

            var orderEvent = new OrderEvent(order, DateTime.UtcNow, Orders.Fees.OrderFee.Zero)
            {
                Status = OrderStatus.Submitted,
                FillPrice = 0,
                FillQuantity = 0
            };

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(orderEvent);

            // Assert - No fee for submitted orders
            Assert.AreEqual(0m, fee.Value.Amount);
        }

        #endregion

        #region Edge Cases

        [Test]
        public void CalculatesFee_NullOrder_ReturnsZero()
        {
            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(null, 100m, 10);

            // Assert
            Assert.AreEqual(0m, fee.Value.Amount);
        }

        [Test]
        public void CalculatesFee_ZeroQuantity_ReturnsMinimumOrZero()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                0,
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, 0);

            // Assert - Zero quantity = zero order value = zero fee
            Assert.AreEqual(0m, fee.Value.Amount);
        }

        [Test]
        public void CalculatesFee_ShortOrder_CalculatesSameFee()
        {
            // Arrange
            var order = new MarketOrder(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                -100, // Short
                DateTime.UtcNow
            );

            // Act
            var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, -100);

            // Assert - Fee should be same regardless of direction
            Assert.AreEqual(400m, fee.Value.Amount);
        }

        [Test]
        public void GetFeeDescription_ReturnsCorrectDescriptions()
        {
            // Act & Assert
            Assert.AreEqual("Spread-based pricing (no commission)",
                IGOrderFeeCalculator.GetFeeDescription(SecurityType.Forex));

            Assert.AreEqual("0.1% commission (minimum £10 GBP)",
                IGOrderFeeCalculator.GetFeeDescription(SecurityType.Index));

            Assert.AreEqual("0.1% commission (minimum £10 GBP)",
                IGOrderFeeCalculator.GetFeeDescription(SecurityType.Cfd));

            Assert.AreEqual("0.1% commission (minimum £10 GBP)",
                IGOrderFeeCalculator.GetFeeDescription(SecurityType.Equity));

            Assert.AreEqual("Spread-based pricing (no commission)",
                IGOrderFeeCalculator.GetFeeDescription(SecurityType.Crypto));
        }

        #endregion
    }
}
