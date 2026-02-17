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
using NUnit.Framework;
using QuantConnect.Interfaces;
using QuantConnect.Packets;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG configuration")]
    public class IGBrokerageFactoryTests
    {
        [Test]
        public void InitializesFactoryFromComposer()
        {
            using var factory = Composer.Instance.Single<IBrokerageFactory>(
                instance => instance.BrokerageType == typeof(IGBrokerage)
            );

            Assert.IsNotNull(factory, "Factory should be discoverable via Composer");
            Assert.AreEqual(typeof(IGBrokerage), factory.BrokerageType);
        }

        [Test]
        public void CreatesBrokerage_WithValidConfiguration_Succeeds()
        {
            var factory = new IGBrokerageFactory();
            var job = CreateTestJob();

            var brokerage = factory.CreateBrokerage(job, null);

            Assert.IsNotNull(brokerage);
            Assert.IsInstanceOf<IGBrokerage>(brokerage);
            brokerage.Dispose();
        }

        [Test]
        public void GetsBrokerageModel_ReturnsCorrectType()
        {
            var factory = new IGBrokerageFactory();
            var model = factory.GetBrokerageModel(null);
            Assert.IsNotNull(model);
        }

        [Test]
        public void BrokerageType_ReturnsCorrectType()
        {
            var factory = new IGBrokerageFactory();
            Assert.AreEqual(typeof(IGBrokerage), factory.BrokerageType);
        }

        [Test]
        public void ParsesBrokerageData_ExtractsAllFields()
        {
            var factory = new IGBrokerageFactory();
            var brokerageData = new Dictionary<string, string>
            {
                { "ig-api-url", "https://api.ig.com/gateway/deal" },
                { "ig-api-key", "my-key-12345" },
                { "ig-username", "my-username" },
                { "ig-password", "my-password" },
                { "ig-account-id", "ABC123" },
                { "ig-environment", "live" }
            };

            var job = new LiveNodePacket { BrokerageData = brokerageData };
            var brokerage = factory.CreateBrokerage(job, null) as IGBrokerage;

            Assert.IsNotNull(brokerage);
            brokerage.Dispose();
        }

        [Test]
        public void CreatesBrokerage_MultipleTimes_CreatesIndependentInstances()
        {
            var factory = new IGBrokerageFactory();
            var job = CreateTestJob();

            var brokerage1 = factory.CreateBrokerage(job, null);
            var brokerage2 = factory.CreateBrokerage(job, null);

            Assert.AreNotSame(brokerage1, brokerage2);

            brokerage1.Dispose();
            brokerage2.Dispose();
        }

        [Test]
        public void Dispose_DisposesFactory_Succeeds()
        {
            var factory = new IGBrokerageFactory();
            Assert.DoesNotThrow(() => factory.Dispose());
        }

        private static LiveNodePacket CreateTestJob()
        {
            return new LiveNodePacket
            {
                BrokerageData = new Dictionary<string, string>
                {
                    { "ig-api-url", "https://demo-api.ig.com/gateway/deal" },
                    { "ig-api-key", "test-key" },
                    { "ig-username", "test-user" },
                    { "ig-password", "test-pass" },
                    { "ig-account-id", "" },
                    { "ig-environment", "demo" }
                }
            };
        }
    }
}
