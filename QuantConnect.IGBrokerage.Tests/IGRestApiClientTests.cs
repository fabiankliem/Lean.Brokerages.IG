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
            // Arrange
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGRestApiClientTests: Credentials not configured in config.json");
            }

            var client = new IGRestApiClient(apiUrl, apiKey);

            // Act
            var loginResponse = client.Login(identifier, password);
            var cst = loginResponse.Cst;
            var securityToken = loginResponse.SecurityToken;
            var lightstreamerEndpoint = loginResponse.LightstreamerEndpoint;

            // Assert
            Assert.IsNotNull(cst, "CST token should not be null");
            Assert.IsNotNull(securityToken, "Security token should not be null");
            Assert.IsNotNull(lightstreamerEndpoint, "Lightstreamer endpoint should not be null");
            Assert.IsFalse(string.IsNullOrEmpty(cst), "CST token should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(securityToken), "Security token should not be empty");
            Assert.IsFalse(string.IsNullOrEmpty(lightstreamerEndpoint), "Lightstreamer endpoint should not be empty");

            // Verify token format (CST and X-SECURITY-TOKEN should be alphanumeric strings)
            Assert.IsTrue(cst.Length > 10, "CST token should be reasonably long");
            Assert.IsTrue(securityToken.Length > 10, "Security token should be reasonably long");
            Assert.IsTrue(lightstreamerEndpoint.StartsWith("https://"),
                "Lightstreamer endpoint should be HTTPS URL");
        }

        [Test]
        public void Authenticate_InvalidCredentials_ThrowsException()
        {
            // Arrange
            var apiUrl = "https://demo-api.ig.com/gateway/deal";
            var apiKey = "invalid-key-12345";
            var client = new IGRestApiClient(apiUrl, apiKey);

            // Act & Assert
            Assert.Throws<Exception>(() =>
                client.Login("invalid-user", "invalid-password"),
                "Authentication with invalid credentials should throw exception"
            );
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void MakesAuthenticatedRequest_AfterAuthentication_Succeeds()
        {
            // Arrange
            var client = CreateAuthenticatedClient();

            // Act
            var accounts = client.GetAccounts();

            // Assert
            Assert.IsNotNull(accounts, "Accounts response should not be null");
            Assert.IsNotEmpty(accounts, "Should have at least one account");

            // Verify account structure
            foreach (var account in accounts)
            {
                Assert.IsNotNull(account.AccountId, "Account ID should not be null");
                Assert.IsNotNull(account.Currency, "Account currency should not be null");
                Assert.IsNotNull(account.Balance, "Account balance should not be null");
            }
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void Authentication_TokensWorkForMultipleRequests()
        {
            // Arrange
            var client = CreateAuthenticatedClient();

            // Act - Make multiple requests with same authentication
            var accounts1 = client.GetAccounts();
            var accounts2 = client.GetAccounts();

            // Assert
            Assert.IsNotNull(accounts1, "First request should succeed");
            Assert.IsNotNull(accounts2, "Second request should succeed");
            Assert.AreEqual(accounts1.Count, accounts2.Count,
                "Should return same account count across requests");
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void GetAccounts_ReturnsAccountDetails()
        {
            // Arrange
            var client = CreateAuthenticatedClient();

            // Act
            var accounts = client.GetAccounts();

            // Assert
            Assert.IsNotEmpty(accounts, "Should have accounts");

            var firstAccount = accounts[0];
            Assert.Greater(firstAccount.Balance.Available, 0m,
                "Demo account should have available balance");

            // Log account details for visibility
            Console.WriteLine($"Account ID: {firstAccount.AccountId}");
            Console.WriteLine($"Currency: {firstAccount.Currency}");
            Console.WriteLine($"Available: {firstAccount.Balance.Available}");
            Console.WriteLine($"Deposit: {firstAccount.Balance.Deposit}");
        }

        [Test]
        public void Constructor_NullApiUrl_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new IGRestApiClient(null, "test-key"),
                "Constructor should validate API URL"
            );
        }

        [Test]
        public void Constructor_EmptyApiKey_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() =>
                new IGRestApiClient("https://demo-api.ig.com/gateway/deal", ""),
                "Constructor should validate API key"
            );
        }

        [Test, Explicit("Requires valid IG Markets credentials")]
        public void Logout_AfterAuthentication_Succeeds()
        {
            // Arrange
            var client = CreateAuthenticatedClient();

            // Act - Logout should not throw
            Assert.DoesNotThrow(() => client.Logout(),
                "Logout should succeed after authentication");

            // After logout, subsequent requests should fail
            Assert.Throws<Exception>(() => client.GetAccounts(),
                "Requests after logout should fail");
        }

        #region Helper Methods

        /// <summary>
        /// Creates an authenticated REST API client for testing
        /// </summary>
        private IGRestApiClient CreateAuthenticatedClient()
        {
            var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
            {
                Assert.Ignore("IGRestApiClientTests: Credentials not configured in config.json");
            }

            var client = new IGRestApiClient(apiUrl, apiKey);
            var loginResponse = client.Login(identifier, password);
            client.SetSessionTokens(loginResponse.Cst, loginResponse.SecurityToken);

            return client;
        }

        #endregion
    }
}
