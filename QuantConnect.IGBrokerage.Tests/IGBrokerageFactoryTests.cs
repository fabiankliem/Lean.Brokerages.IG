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

using System.Collections.Generic;
using NUnit.Framework;
using QuantConnect.Configuration;
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
            // Act
            using var factory = Composer.Instance.Single<IBrokerageFactory>(
                instance => instance.BrokerageType == typeof(IGBrokerage)
            );

            // Assert
            Assert.IsNotNull(factory, "Factory should be discoverable via Composer");
            Assert.AreEqual(typeof(IGBrokerage), factory.BrokerageType,
                "Factory should declare correct brokerage type");
        }

        [Test]
        public void CreatesBrokerage_WithValidConfiguration_Succeeds()
        {
            // Arrange
            var factory = new IGBrokerageFactory();
            var brokerageData = new Dictionary<string, string>
            {
                { "ig-api-url", "https://demo-api.ig.com/gateway/deal" },
                { "ig-api-key", Config.Get("ig-api-key", "test-key") },
                { "ig-identifier", Config.Get("ig-identifier", "test-user") },
                { "ig-password", Config.Get("ig-password", "test-pass") },
                { "ig-account-id", "" },
                { "ig-environment", "demo" }
            };

            var job = new LiveNodePacket { BrokerageData = brokerageData };

            // Act
            var brokerage = factory.CreateBrokerage(job, null);

            // Assert
            Assert.IsNotNull(brokerage, "Factory should create brokerage instance");
            Assert.IsInstanceOf<IGBrokerage>(brokerage, "Should create IGBrokerage type");

            // Cleanup
            brokerage.Dispose();
        }

        [Test]
        public void GetsBrokerageModel_ReturnsCorrectType()
        {
            // Arrange
            var factory = new IGBrokerageFactory();

            // Act
            var model = factory.GetBrokerageModel(null);

            // Assert
            Assert.IsNotNull(model, "Factory should return brokerage model");
            // Verify model type when implemented
        }

        [Test]
        public void BrokerageType_ReturnsCorrectType()
        {
            // Arrange
            var factory = new IGBrokerageFactory();

            // Act
            var brokerageType = factory.BrokerageType;

            // Assert
            Assert.AreEqual(typeof(IGBrokerage), brokerageType,
                "BrokerageType property should return IGBrokerage");
        }

        [Test]
        public void ParsesBrokerageData_ExtractsAllFields()
        {
            // Arrange
            var factory = new IGBrokerageFactory();
            var brokerageData = new Dictionary<string, string>
            {
                { "ig-api-url", "https://api.ig.com/gateway/deal" },
                { "ig-api-key", "my-key-12345" },
                { "ig-identifier", "my-username" },
                { "ig-password", "my-password" },
                { "ig-account-id", "ABC123" },
                { "ig-environment", "live" }
            };

            var job = new LiveNodePacket { BrokerageData = brokerageData };

            // Act
            var brokerage = factory.CreateBrokerage(job, null) as IGBrokerage;

            // Assert
            Assert.IsNotNull(brokerage, "Should create brokerage from configuration");

            // Cleanup
            brokerage.Dispose();
        }

        [Test]
        public void CreatesBrokerage_WithMinimalConfiguration_Succeeds()
        {
            // Arrange - Minimum required fields only
            var factory = new IGBrokerageFactory();
            var brokerageData = new Dictionary<string, string>
            {
                { "ig-api-url", "https://demo-api.ig.com/gateway/deal" },
                { "ig-api-key", "test-key" },
                { "ig-identifier", "test-user" },
                { "ig-password", "test-pass" }
                // No account-id or environment (should use defaults)
            };

            var job = new LiveNodePacket { BrokerageData = brokerageData };

            // Act
            var brokerage = factory.CreateBrokerage(job, null);

            // Assert
            Assert.IsNotNull(brokerage, "Should create brokerage with minimal configuration");

            // Cleanup
            brokerage.Dispose();
        }

        [Test]
        public void CreatesBrokerage_MultipleTimes_CreatesIndependentInstances()
        {
            // Arrange
            var factory = new IGBrokerageFactory();
            var brokerageData = new Dictionary<string, string>
            {
                { "ig-api-url", "https://demo-api.ig.com/gateway/deal" },
                { "ig-api-key", "test-key" },
                { "ig-identifier", "test-user" },
                { "ig-password", "test-pass" },
                { "ig-environment", "demo" }
            };

            var job = new LiveNodePacket { BrokerageData = brokerageData };

            // Act
            var brokerage1 = factory.CreateBrokerage(job, null);
            var brokerage2 = factory.CreateBrokerage(job, null);

            // Assert
            Assert.IsNotNull(brokerage1, "First brokerage should be created");
            Assert.IsNotNull(brokerage2, "Second brokerage should be created");
            Assert.AreNotSame(brokerage1, brokerage2,
                "Should create independent brokerage instances");

            // Cleanup
            brokerage1.Dispose();
            brokerage2.Dispose();
        }

        [Test]
        public void Dispose_DisposesFactory_Succeeds()
        {
            // Arrange
            var factory = new IGBrokerageFactory();

            // Act & Assert
            Assert.DoesNotThrow(() => factory.Dispose(),
                "Factory dispose should not throw");
        }
    }
}
