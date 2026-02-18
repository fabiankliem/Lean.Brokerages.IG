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

using System.Collections.Generic;
using QuantConnect.Benchmarks;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Orders.Slippage;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Brokerages
{
    /// <summary>
    /// Provides IG Markets brokerage model specific properties and methods
    /// </summary>
    /// <remarks>
    /// IG Markets supports trading in Forex, Indices, Commodities, Crypto, and Share CFDs.
    /// This model configures the appropriate fee models, fill models, and order validation
    /// for IG Markets trading.
    /// </remarks>
    public class IGBrokerageModel : DefaultBrokerageModel
    {
        /// <summary>
        /// The default markets for IG brokerage
        /// </summary>
        public new static readonly IReadOnlyDictionary<SecurityType, string> DefaultMarketMap = new Dictionary<SecurityType, string>
        {
            { SecurityType.Forex, IG.IGSymbolMapper.MarketName },
            { SecurityType.Cfd, IG.IGSymbolMapper.MarketName },
            { SecurityType.Crypto, IG.IGSymbolMapper.MarketName },
            { SecurityType.Index, IG.IGSymbolMapper.MarketName },
            { SecurityType.Equity, IG.IGSymbolMapper.MarketName }
        }.ToReadOnlyDictionary();

        /// <summary>
        /// Gets a map of the default markets to be used for each security type
        /// </summary>
        public override IReadOnlyDictionary<SecurityType, string> DefaultMarkets => DefaultMarketMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="IGBrokerageModel"/> class
        /// </summary>
        /// <param name="accountType">The type of account to be modelled, defaults to Margin</param>
        public IGBrokerageModel(AccountType accountType = AccountType.Margin)
            : base(accountType)
        {
        }

        /// <summary>
        /// Returns true if the brokerage could accept this order.
        /// </summary>
        public override bool CanSubmitOrder(Security security, Order order, out BrokerageMessageEvent message)
        {
            message = null;

            if (!IsSecurityTypeSupported(security.Type))
            {
                message = new BrokerageMessageEvent(
                    BrokerageMessageType.Warning,
                    "UnsupportedSecurityType",
                    $"IG does not support {security.Type} security type."
                );
                return false;
            }

            if (!IsOrderTypeSupported(order.Type))
            {
                message = new BrokerageMessageEvent(
                    BrokerageMessageType.Warning,
                    "UnsupportedOrderType",
                    $"IG does not support {order.Type} order type."
                );
                return false;
            }

            return base.CanSubmitOrder(security, order, out message);
        }

        /// <summary>
        /// Returns true if the brokerage would allow updating the order as specified by the request
        /// </summary>
        public override bool CanUpdateOrder(Security security, Order order, UpdateOrderRequest request, out BrokerageMessageEvent message)
        {
            message = null;

            if (order.Type == OrderType.Market)
            {
                message = new BrokerageMessageEvent(
                    BrokerageMessageType.Warning,
                    "OrderUpdateNotSupported",
                    "IG does not support updating market orders."
                );
                return false;
            }

            return base.CanUpdateOrder(security, order, request, out message);
        }

        /// <summary>
        /// Gets a new fill model for the specified security
        /// </summary>
        public override IFillModel GetFillModel(Security security)
        {
            return new ImmediateFillModel();
        }

        /// <summary>
        /// Gets the fee model for the specified security
        /// </summary>
        public override IFeeModel GetFeeModel(Security security)
        {
            return new IGFeeModel();
        }

        /// <summary>
        /// Gets the slippage model for the specified security
        /// </summary>
        public override ISlippageModel GetSlippageModel(Security security)
        {
            return new ConstantSlippageModel(0m);
        }

        /// <summary>
        /// Gets the benchmark for the specified algorithm
        /// </summary>
        public override IBenchmark GetBenchmark(SecurityManager securities)
        {
            var symbol = Symbol.Create("SPY", SecurityType.Equity, Market.USA);
            return SecurityBenchmark.CreateInstance(securities, symbol);
        }

        private static bool IsSecurityTypeSupported(SecurityType securityType)
        {
            return securityType == SecurityType.Forex ||
                   securityType == SecurityType.Cfd ||
                   securityType == SecurityType.Crypto ||
                   securityType == SecurityType.Index ||
                   securityType == SecurityType.Equity;
        }

        private static bool IsOrderTypeSupported(OrderType orderType)
        {
            return orderType == OrderType.Market ||
                   orderType == OrderType.Limit ||
                   orderType == OrderType.StopMarket ||
                   orderType == OrderType.StopLimit;
        }
    }
}
