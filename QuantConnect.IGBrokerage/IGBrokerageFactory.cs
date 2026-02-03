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
using System.Collections.Generic;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// Factory for creating IG Markets brokerage instances
    /// </summary>
    public class IGBrokerageFactory : BrokerageFactory
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="IGBrokerageFactory"/> class
        /// </summary>
        public IGBrokerageFactory() : base(typeof(IGBrokerage))
        {
        }

        /// <summary>
        /// Gets brokerage configuration data from config file
        /// </summary>
        /// <remarks>
        /// The implementation of this property will create the brokerage data dictionary required for
        /// running live jobs. See <see cref="IJobQueueHandler.NextJob"/>
        /// </remarks>
        public override Dictionary<string, string> BrokerageData => new Dictionary<string, string>
        {
            { "ig-api-url", Config.Get("ig-api-url", "") },
            { "ig-identifier", Config.Get("ig-identifier") },
            { "ig-password", Config.Get("ig-password") },
            { "ig-api-key", Config.Get("ig-api-key") },
            { "ig-account-id", Config.Get("ig-account-id") },
            { "ig-environment", Config.Get("ig-environment", "demo") } // "demo" or "live"
        };

        /// <summary>
        /// Gets the brokerage model for IG Markets
        /// </summary>
        /// <param name="orderProvider">The order provider</param>
        public override IBrokerageModel GetBrokerageModel(IOrderProvider orderProvider)
        {
            return new IGBrokerageModel();
        }

        /// <summary>
        /// Creates a new IGBrokerage instance
        /// </summary>
        /// <param name="job">The job packet to create the brokerage for</param>
        /// <param name="algorithm">The algorithm instance</param>
        /// <returns>A new brokerage instance</returns>
        public override IBrokerage CreateBrokerage(LiveNodePacket job, IAlgorithm algorithm)
        {
            var errors = new List<string>();

            // Get configuration from job packet (overrides config file)
            var apiUrl = Read<string>(job.BrokerageData, "ig-api-url", errors);
            var identifier = Read<string>(job.BrokerageData, "ig-identifier", errors);
            var password = Read<string>(job.BrokerageData, "ig-password", errors);
            var apiKey = Read<string>(job.BrokerageData, "ig-api-key", errors);
            var accountId = Read<string>(job.BrokerageData, "ig-account-id", errors);
            var environment = Read<string>(job.BrokerageData, "ig-environment", errors);

            // Validate required fields
            if (string.IsNullOrEmpty(identifier))
            {
                errors.Add("ig-identifier");
            }
            if (string.IsNullOrEmpty(password))
            {
                errors.Add("ig-password");
            }
            if (string.IsNullOrEmpty(apiKey))
            {
                errors.Add("ig-api-key");
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException(
                    $"IGBrokerageFactory.CreateBrokerage: Missing required configuration: {string.Join(", ", errors)}");
            }

            // Determine API URL based on environment if not explicitly provided
            if (string.IsNullOrEmpty(apiUrl))
            {
                apiUrl = environment?.ToLowerInvariant() == "live"
                    ? "https://api.ig.com/gateway/deal"
                    : "https://demo-api.ig.com/gateway/deal";
            }

            var brokerage = new IGBrokerage(
                apiUrl,
                identifier,
                password,
                apiKey,
                accountId,
                algorithm,
                Composer.Instance.GetPart<IDataAggregator>()
            );

            // Register as data queue handler for live data
            Composer.Instance.AddPart<IDataQueueHandler>(brokerage);

            return brokerage;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            // Cleanup if needed
        }
    }
}
