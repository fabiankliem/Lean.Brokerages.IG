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
using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Brokerages.IG.Api;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture]
    public class IGRestApiClientTests
    {
        [Test, Explicit("Requires valid IG Markets credentials")]
        public void Authenticate_ValidCredentials_Succeeds()
        {
            var client = CreateAuthenticatedClient();

            // Verify tokens are set on the client
            Assert.IsNotNull(client.Cst, "CST token should not be null");
            Assert.IsNotNull(client.SecurityToken, "Security token should not be null");
            Assert.IsNotNull(client.LightstreamerEndpoint, "Lightstreamer endpoint should not be null");
            Assert.IsFalse(string.IsNullOrEmpty(client.Cst), "CST token should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(client.SecurityToken), "Security token should not be empty");
            Assert.IsTrue(client.LightstreamerEndpoint.StartsWith("https://"),
                "Lightstreamer endpoint should be HTTPS URL");
        }

        [Test]
        public void Authenticate_InvalidCredentials_ThrowsException()
        {
            var client = new IGRestApiClient("https://demo-api.ig.com/gateway/deal", "invalid-key-12345", "");

            Assert.Throws<Exception>(() =>
                client.Login("invalid-user", "invalid-password"),
                "Authentication with invalid credentials should throw exception"
            );
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void GetAccountBalance_ReturnsAccountDetails()
        {
            var client = CreateAuthenticatedClient();

            var account = client.GetAccountBalance();

            Assert.IsNotNull(account, "Account should not be null");
            Assert.IsNotNull(account.AccountId, "Account ID should not be null");
            Assert.IsNotNull(account.Currency, "Account currency should not be null");
            Assert.IsNotNull(account.Balance, "Account balance should not be null");
            Assert.Greater(account.Balance.Available, 0m, "Demo account should have available balance");

            Console.WriteLine($"Account ID: {account.AccountId}");
            Console.WriteLine($"Currency: {account.Currency}");
            Console.WriteLine($"Available: {account.Balance.Available}");
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void Authentication_TokensWorkForMultipleRequests()
        {
            var client = CreateAuthenticatedClient();

            var account1 = client.GetAccountBalance();
            var account2 = client.GetAccountBalance();

            Assert.IsNotNull(account1, "First request should succeed");
            Assert.IsNotNull(account2, "Second request should succeed");
            Assert.AreEqual(account1.AccountId, account2.AccountId,
                "Should return same account across requests");
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void Logout_AfterAuthentication_Succeeds()
        {
            var client = CreateAuthenticatedClient();

            Assert.DoesNotThrow(() => client.Logout(),
                "Logout should succeed after authentication");

            Assert.Throws<Exception>(() => client.GetAccountBalance(),
                "Requests after logout should fail");
        }

        #region Helper Methods

        private IGRestApiClient CreateAuthenticatedClient()
        {
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var username = Config.Get("ig-username");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGRestApiClientTests: Credentials not configured in config.json");
            }

            var client = new IGRestApiClient(apiUrl, apiKey, accountId);
            client.Login(username, password);

            return client;
        }

        #endregion
    }
}
