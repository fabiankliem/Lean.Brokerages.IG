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
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Securities;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// Calculates order fees for IG Markets according to their pricing structure
    /// </summary>
    /// <remarks>
    /// IG Markets Fee Structure:
    /// - Forex: Spread-based pricing (no commission)
    /// - CFD/Index/Equity: 0.1% commission with £10 minimum
    /// - Crypto: Spread-based pricing (no commission)
    ///
    /// Note: Fees are charged in GBP regardless of the traded instrument currency
    /// </remarks>
    public static class IGOrderFeeCalculator
    {
        private const decimal CommissionRate = 0.001m; // 0.1%
        private const decimal MinimumCommission = 10m; // £10 GBP
        private const string FeeCurrency = "GBP";

        /// <summary>
        /// Calculate fee for an order based on fill price and quantity
        /// </summary>
        /// <param name="order">The order being filled</param>
        /// <param name="fillPrice">The fill price</param>
        /// <param name="fillQuantity">The fill quantity</param>
        /// <returns>Order fee in GBP</returns>
        public static OrderFee CalculateFee(Order order, decimal fillPrice, decimal fillQuantity)
        {
            if (order == null)
            {
                return OrderFee.Zero;
            }

            // Calculate absolute order value
            var orderValue = Math.Abs(fillPrice * fillQuantity);

            decimal feeAmount = 0m;

            switch (order.SecurityType)
            {
                case SecurityType.Forex:
                    // Forex: Spread-based pricing, no commission
                    // The spread cost is already included in the execution price
                    return OrderFee.Zero;

                case SecurityType.Index:
                case SecurityType.Cfd:
                    // Index/CFD: 0.1% commission with £10 minimum
                    feeAmount = CalculateCommissionFee(orderValue);
                    break;

                case SecurityType.Equity:
                    // Equity: 0.1% commission with £10 minimum
                    feeAmount = CalculateCommissionFee(orderValue);
                    break;

                case SecurityType.Crypto:
                    // Crypto: Spread-based pricing, no commission
                    return OrderFee.Zero;

                case SecurityType.Future:
                case SecurityType.Option:
                case SecurityType.FutureOption:
                case SecurityType.IndexOption:
                    // Not currently supported
                    return OrderFee.Zero;

                default:
                    // Unknown security type, return zero
                    return OrderFee.Zero;
            }

            return new OrderFee(new CashAmount(feeAmount, FeeCurrency));
        }

        /// <summary>
        /// Calculate fee for a filled order event
        /// </summary>
        /// <param name="orderEvent">The order event</param>
        /// <returns>Order fee in GBP</returns>
        public static OrderFee CalculateFee(OrderEvent orderEvent)
        {
            if (orderEvent?.Order == null)
            {
                return OrderFee.Zero;
            }

            // Only calculate fees for filled or partially filled orders
            if (orderEvent.Status != OrderStatus.Filled &&
                orderEvent.Status != OrderStatus.PartiallyFilled)
            {
                return OrderFee.Zero;
            }

            return CalculateFee(
                orderEvent.Order,
                orderEvent.FillPrice,
                orderEvent.FillQuantity
            );
        }

        /// <summary>
        /// Calculate commission-based fee (0.1% with £10 minimum)
        /// </summary>
        /// <param name="orderValue">The absolute order value</param>
        /// <returns>Fee amount in GBP</returns>
        private static decimal CalculateCommissionFee(decimal orderValue)
        {
            if (orderValue <= 0)
            {
                return 0m;
            }

            // Calculate 0.1% commission
            var commission = orderValue * CommissionRate;

            // Apply minimum commission of £10
            return Math.Max(commission, MinimumCommission);
        }

        /// <summary>
        /// Get a description of IG Markets fee structure for a security type
        /// </summary>
        /// <param name="securityType">The security type</param>
        /// <returns>Fee description</returns>
        public static string GetFeeDescription(SecurityType securityType)
        {
            switch (securityType)
            {
                case SecurityType.Forex:
                    return "Spread-based pricing (no commission)";

                case SecurityType.Index:
                case SecurityType.Cfd:
                case SecurityType.Equity:
                    return "0.1% commission (minimum £10 GBP)";

                case SecurityType.Crypto:
                    return "Spread-based pricing (no commission)";

                default:
                    return "Fee structure not defined";
            }
        }
    }
}
