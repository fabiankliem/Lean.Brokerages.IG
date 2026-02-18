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

using Newtonsoft.Json;
using QuantConnect.Brokerages.IG.Models;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using QuantConnect.Util;

namespace QuantConnect.Brokerages.IG.Api
{
    /// <summary>
    /// REST API client for IG Markets trading platform.
    /// Handles authentication, rate limiting, and all REST endpoint interactions.
    /// </summary>
    public class IGRestApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _accountId;
        private readonly RateGate _tradingRateGate;
        private readonly RateGate _nonTradingRateGate;

        private string _cst;
        private string _securityToken;
        private string _lightstreamerEndpoint;

        /// <summary>
        /// Creates a new IG REST API client
        /// </summary>
        /// <param name="baseUrl">IG API base URL (demo or live)</param>
        /// <param name="apiKey">IG API key</param>
        /// <param name="accountId">IG account identifier</param>
        public IGRestApiClient(string baseUrl, string apiKey, string accountId)
        {
            _baseUrl = baseUrl;
            _accountId = accountId;

            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("X-IG-API-KEY", apiKey);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json; charset=UTF-8");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuantConnect/LEAN");

            // IG rate limits: 40 trading requests/min, 60 non-trading requests/min
            _tradingRateGate = new RateGate(40, TimeSpan.FromMinutes(1));
            _nonTradingRateGate = new RateGate(60, TimeSpan.FromMinutes(1));
        }

        #region Authentication

        /// <summary>
        /// Authenticates with IG and stores session tokens.
        /// Throws if authentication fails.
        /// </summary>
        /// <param name="identifier">IG username</param>
        /// <param name="password">IG password</param>
        /// <returns>Login response with account and connection info</returns>
        public IGLoginResponse Login(string identifier, string password)
        {
            var loginRequest = new IGLoginRequest
            {
                Identifier = identifier,
                Password = password
            };

            var httpResponse = SendRawRequest(HttpMethod.Post, IGApiEndpoints.Session, loginRequest, version: 2);

            // Extract tokens from response headers
            _cst = ExtractRequiredHeader(httpResponse, "CST");
            _securityToken = ExtractRequiredHeader(httpResponse, "X-SECURITY-TOKEN");

            // Set tokens as default headers for all subsequent requests
            SetDefaultHeader("CST", _cst);
            SetDefaultHeader("X-SECURITY-TOKEN", _securityToken);

            var body = ReadResponseBody(httpResponse);
            var session = JsonConvert.DeserializeObject<IGSessionResponse>(body);

            _lightstreamerEndpoint = session.LightstreamerEndpoint;

            return new IGLoginResponse
            {
                LightstreamerEndpoint = _lightstreamerEndpoint,
                AccountId = session.CurrentAccountId,
                ClientId = session.ClientId
            };
        }

        /// <summary>
        /// Logs out from IG, ending the current session
        /// </summary>
        public void Logout()
        {
            try
            {
                SendRawRequest(HttpMethod.Delete, IGApiEndpoints.Session);
            }
            catch (Exception ex)
            {
                Log.Error($"IGRestApiClient.Logout(): {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a new Lightstreamer streaming client using the session tokens from Login.
        /// Must be called after Login() has been called successfully.
        /// </summary>
        /// <param name="accountId">IG account identifier for trade/account subscriptions</param>
        /// <returns>A configured and ready-to-connect streaming client</returns>
        public IGLightstreamerClient CreateStreamingClient(string accountId)
        {
            if (string.IsNullOrEmpty(_cst) || string.IsNullOrEmpty(_securityToken))
            {
                throw new InvalidOperationException(
                    "Cannot create streaming client before login. Call Login() first.");
            }

            return new IGLightstreamerClient(_lightstreamerEndpoint, _cst, _securityToken, accountId);
        }

        #endregion

        #region Account

        /// <summary>
        /// Gets account balance information for the configured account
        /// </summary>
        /// <returns>Account data for the current account</returns>
        public IGAccountData GetAccountBalance()
        {
            var response = SendRequest<IGAccountsResponse>(HttpMethod.Get, IGApiEndpoints.Accounts);
            return response.Accounts.FirstOrDefault(a => a.AccountId == _accountId)
                ?? throw new InvalidOperationException($"Account {_accountId} not found in IG response");
        }

        #endregion

        #region Positions

        /// <summary>
        /// Gets all open positions for the current session
        /// </summary>
        /// <returns>Open positions</returns>
        public IEnumerable<IGPosition> GetPositions()
        {
            var response = SendRequest<IGPositionsResponse>(HttpMethod.Get, IGApiEndpoints.Positions, version: 2);

            foreach (var p in response.Positions)
            {
                yield return new IGPosition
                {
                    DealId = p.Position.DealId,
                    Epic = p.Market.Epic,
                    Direction = p.Position.Direction,
                    Size = p.Position.Size,
                    OpenLevel = p.Position.OpenLevel,
                    CurrentLevel = p.Market.Bid ?? 0,
                    Currency = p.Position.Currency,
                    UnrealizedPnL = p.Position.Profit
                };
            }
        }

        #endregion

        #region Orders

        /// <summary>
        /// Gets all working (pending) orders
        /// </summary>
        /// <returns>Working orders, empty enumerable if none</returns>
        public IEnumerable<IGWorkingOrder> GetWorkingOrders()
        {
            var response = SendRequest<IGWorkingOrdersResponse>(HttpMethod.Get, IGApiEndpoints.WorkingOrders, version: 2);
            if (response.WorkingOrders == null)
            {
                return Enumerable.Empty<IGWorkingOrder>();
            }

            return response.WorkingOrders.Select(wo => new IGWorkingOrder
            {
                DealId = wo.WorkingOrderData.DealId,
                Epic = wo.MarketData.Epic,
                Direction = wo.WorkingOrderData.Direction,
                Size = wo.WorkingOrderData.Size,
                Level = wo.WorkingOrderData.Level,
                OrderType = wo.WorkingOrderData.Type,
                CreatedDate = wo.WorkingOrderData.CreatedDate
            });
        }

        /// <summary>
        /// Places a new order
        /// </summary>
        /// <param name="request">Order placement request</param>
        /// <returns>Deal reference for confirmation polling</returns>
        public string PlaceOrder(IGPlaceOrderRequest request)
        {
            var response = SendRequest<IGDealReferenceResponse>(HttpMethod.Post, IGApiEndpoints.PositionsOtc, request, version: 2, isTradingRequest: true);
            return response.DealReference;
        }

        /// <summary>
        /// Updates an existing working order
        /// </summary>
        /// <param name="request">Order update request with DealId set</param>
        /// <returns>Deal reference for confirmation polling</returns>
        public string UpdateOrder(IGUpdateOrderRequest request)
        {
            var response = SendRequest<IGDealReferenceResponse>(HttpMethod.Put,
                $"{IGApiEndpoints.WorkingOrders}/{request.DealId}", request, version: 2, isTradingRequest: true);
            return response.DealReference;
        }

        /// <summary>
        /// Cancels a working order
        /// </summary>
        /// <param name="dealId">Deal ID of the order to cancel</param>
        /// <returns>Deal reference for confirmation polling</returns>
        public string CancelOrder(string dealId)
        {
            var response = SendRequest<IGDealReferenceResponse>(HttpMethod.Delete,
                $"{IGApiEndpoints.WorkingOrders}/{dealId}", isTradingRequest: true);
            return response.DealReference;
        }

        /// <summary>
        /// Gets deal confirmation for a submitted order
        /// </summary>
        /// <param name="dealReference">Deal reference from order submission</param>
        /// <returns>Deal confirmation with status, fill price, and fill size</returns>
        public IGDealConfirmation GetDealConfirmation(string dealReference)
        {
            return SendRequest<IGDealConfirmation>(HttpMethod.Get, $"{IGApiEndpoints.Confirms}/{dealReference}");
        }

        #endregion

        #region Market Data

        /// <summary>
        /// Gets detailed market data for a specific instrument
        /// </summary>
        /// <param name="epic">IG EPIC code for the instrument</param>
        /// <returns>Market details including instrument info, snapshot, and dealing rules</returns>
        public IGMarketDetailsResponse GetMarketData(string epic)
        {
            return SendRequest<IGMarketDetailsResponse>(HttpMethod.Get, $"{IGApiEndpoints.Markets}/{epic}", version: 3);
        }

        /// <summary>
        /// Searches for markets matching a search term
        /// </summary>
        /// <param name="searchTerm">Search term to match against instrument names</param>
        /// <returns>Matching markets</returns>
        public IEnumerable<IGMarketSearchResult> SearchMarkets(string searchTerm)
        {
            var response = SendRequest<IGMarketsSearchResponse>(HttpMethod.Get,
                $"{IGApiEndpoints.MarketSearch}{Uri.EscapeDataString(searchTerm)}");
            return response.Markets ?? Enumerable.Empty<IGMarketSearchResult>();
        }

        #endregion

        #region Historical Data

        /// <summary>
        /// Gets historical price candles for an instrument
        /// </summary>
        /// <param name="epic">IG EPIC code</param>
        /// <param name="resolution">Price resolution (SECOND, MINUTE, HOUR, DAY)</param>
        /// <param name="startDate">Start of the date range</param>
        /// <param name="endDate">End of the date range</param>
        /// <returns>Historical price candles</returns>
        public IEnumerable<IGPriceCandleData> GetHistoricalPrices(string epic, string resolution,
            DateTime startDate, DateTime endDate)
        {
            var url = $"{IGApiEndpoints.Prices}/{epic}?resolution={resolution}" +
                      $"&from={startDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}" +
                      $"&to={endDate.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";

            var response = SendRequest<IGPricesResponse>(HttpMethod.Get, url, version: 3);
            return response.Prices ?? Enumerable.Empty<IGPriceCandleData>();
        }

        #endregion

        #region HTTP Infrastructure

        /// <summary>
        /// Sends a request and deserializes the response to the specified type
        /// </summary>
        private T SendRequest<T>(HttpMethod method, string endpoint, object body = null,
            int version = 1, bool isTradingRequest = false)
        {
            var response = SendRawRequest(method, endpoint, body, version, isTradingRequest);
            var responseBody = ReadResponseBody(response);
            return JsonConvert.DeserializeObject<T>(responseBody);
        }

        /// <summary>
        /// Sends an HTTP request with rate limiting and returns the raw response.
        /// Throws on non-success status codes.
        /// </summary>
        private HttpResponseMessage SendRawRequest(HttpMethod method, string endpoint,
            object body = null, int version = 1, bool isTradingRequest = false)
        {
            // Apply rate limiting
            var rateGate = isTradingRequest ? _tradingRateGate : _nonTradingRateGate;
            rateGate.WaitToProceed();

            var request = new HttpRequestMessage(method, _baseUrl + endpoint);
            request.Headers.Add("VERSION", version.ToString(CultureInfo.InvariantCulture));

            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = _httpClient.SendAsync(request).SynchronouslyAwaitTaskResult();

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = ReadResponseBody(response);
                throw new Exception($"IG API Error: {response.StatusCode} - {errorBody}");
            }

            return response;
        }

        /// <summary>
        /// Reads the response body as a string
        /// </summary>
        private static string ReadResponseBody(HttpResponseMessage response)
        {
            return response.Content.ReadAsStringAsync().SynchronouslyAwaitTaskResult();
        }

        /// <summary>
        /// Extracts a required header value from the response, throwing if missing
        /// </summary>
        private static string ExtractRequiredHeader(HttpResponseMessage response, string headerName)
        {
            if (!response.Headers.TryGetValues(headerName, out var values))
            {
                throw new InvalidOperationException(
                    $"IG login failed: missing '{headerName}' header. Check your credentials and API key.");
            }
            return string.Join("", values);
        }

        /// <summary>
        /// Sets or replaces a default header on the HTTP client
        /// </summary>
        private void SetDefaultHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
            _httpClient.DefaultRequestHeaders.Add(name, value);
        }

        #endregion

        /// <summary>
        /// Disposes HTTP client and rate gates
        /// </summary>
        public void Dispose()
        {
            _httpClient?.Dispose();
            _tradingRateGate?.Dispose();
            _nonTradingRateGate?.Dispose();
        }
    }
}
