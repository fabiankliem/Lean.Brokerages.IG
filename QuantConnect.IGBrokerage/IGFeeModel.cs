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
using QuantConnect.Securities;

namespace QuantConnect.Orders.Fees
{
    /// <summary>
    /// Provides an implementation of <see cref="FeeModel"/> that models IG Markets order fees
    /// </summary>
    /// <remarks>
    /// IG Markets Fee Structure:
    /// - Forex: Spread-based pricing (no commission)
    /// - CFD/Index/Equity: 0.1% commission with £10 minimum
    /// - Crypto: Spread-based pricing (no commission)
    ///
    /// Note: Fees are charged in GBP regardless of the traded instrument currency
    /// </remarks>
    public class IGFeeModel : FeeModel
    {
        private const decimal CommissionRate = 0.001m; // 0.1%
        private const decimal MinimumCommission = 10m; // £10 GBP
        private const string FeeCurrency = "GBP";

        /// <summary>
        /// Get the fee for this order in units of the account currency
        /// </summary>
        public override OrderFee GetOrderFee(OrderFeeParameters parameters)
        {
            if (parameters?.Order == null || parameters.Security == null)
            {
                return OrderFee.Zero;
            }

            var order = parameters.Order;
            var security = parameters.Security;

            var fillPrice = order.Price;
            var fillQuantity = order.AbsoluteQuantity;
            var orderValue = Math.Abs(fillPrice * fillQuantity);

            switch (security.Type)
            {
                case SecurityType.Forex:
                case SecurityType.Crypto:
                    // Spread-based pricing, no commission
                    return OrderFee.Zero;

                case SecurityType.Index:
                case SecurityType.Cfd:
                case SecurityType.Equity:
                    // 0.1% commission with £10 minimum
                    var feeAmount = CalculateCommissionFee(orderValue);
                    return new OrderFee(new CashAmount(feeAmount, FeeCurrency));

                default:
                    return OrderFee.Zero;
            }
        }

        private static decimal CalculateCommissionFee(decimal orderValue)
        {
            if (orderValue <= 0)
            {
                return 0m;
            }

            var commission = orderValue * CommissionRate;
            return Math.Max(commission, MinimumCommission);
        }
    }
}
