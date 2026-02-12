// TODO: Add License
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Brokerages.IG.Models;
using QuantConnect.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace QuantConnect.Brokerages.IG.Api
{
    /// <summary>
    /// REST API client for IG Markets
    /// </summary>
    public class IGRestApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        private string _cst;
        private string _securityToken;

        public IGRestApiClient(string baseUrl, string apiKey)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("API URL must not be null or empty", nameof(baseUrl));
            }
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API key must not be null or empty", nameof(apiKey));
            }

            _baseUrl = baseUrl;
            _apiKey = apiKey;

            var handler = new HttpClientHandler
            {
                UseCookies = true,
                CookieContainer = new System.Net.CookieContainer()
            };
            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("X-IG-API-KEY", apiKey);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json; charset=UTF-8");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "QuantConnect/LEAN");
        }

        /// <summary>
        /// Sets session tokens for authenticated requests
        /// </summary>
        public void SetSessionTokens(string cst, string securityToken)
        {
            _cst = cst;
            _securityToken = securityToken;
        }

        #region Authentication

        /// <summary>
        /// Logs in to IG and retrieves session tokens
        /// </summary>
        public IGLoginResponse Login(string identifier, string password)
        {
            var request = new
            {
                identifier = identifier,
                password = password
            };

            var response = SendRequest(HttpMethod.Post, IGApiEndpoints.Session, request, version: 2);

            // Extract tokens from headers
            var cst = GetHeader(response, "CST");
            var securityToken = GetHeader(response, "X-SECURITY-TOKEN");

            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            return new IGLoginResponse
            {
                Cst = cst,
                SecurityToken = securityToken,
                LightstreamerEndpoint = json["lightstreamerEndpoint"]?.ToString(),
                AccountId = json["currentAccountId"]?.ToString(),
                ClientId = json["clientId"]?.ToString()
            };
        }

        /// <summary>
        /// Logs out from IG
        /// </summary>
        public void Logout()
        {
            try
            {
                SendRequest(HttpMethod.Delete, IGApiEndpoints.Session);
            }
            catch (Exception ex)
            {
                Log.Error($"IGRestApiClient.Logout(): Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Switches to a different account
        /// </summary>
        public void SwitchAccount(string accountId)
        {
            var request = new { accountId = accountId };
            SendRequest(HttpMethod.Put, IGApiEndpoints.Session, request);
        }

        #endregion

        #region Account

        /// <summary>
        /// Gets account information
        /// </summary>
        public List<IGAccount> GetAccounts()
        {
            var response = SendRequest(HttpMethod.Get, IGApiEndpoints.Accounts);
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            var accounts = new List<IGAccount>();
            foreach (var acc in json["accounts"])
            {
                accounts.Add(new IGAccount
                {
                    AccountId = acc["accountId"]?.ToString(),
                    AccountName = acc["accountName"]?.ToString(),
                    AccountType = acc["accountType"]?.ToString(),
                    Currency = acc["currency"]?.ToString(),
                    Balance = new IGAccountBalance
                    {
                        Available = acc["balance"]?["available"]?.Value<decimal>() ?? 0,
                        Balance = acc["balance"]?["balance"]?.Value<decimal>() ?? 0,
                        Deposit = acc["balance"]?["deposit"]?.Value<decimal>() ?? 0,
                        ProfitLoss = acc["balance"]?["profitLoss"]?.Value<decimal>() ?? 0
                    }
                });
            }

            return accounts;
        }

        #endregion

        #region Positions

        /// <summary>
        /// Gets all open positions
        /// </summary>
        public List<IGPosition> GetPositions()
        {
            var response = SendRequest(HttpMethod.Get, IGApiEndpoints.Positions);
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            var positions = new List<IGPosition>();
            foreach (var pos in json["positions"])
            {
                positions.Add(new IGPosition
                {
                    DealId = pos["position"]?["dealId"]?.ToString(),
                    Epic = pos["market"]?["epic"]?.ToString(),
                    Direction = pos["position"]?["direction"]?.ToString(),
                    Size = pos["position"]?["size"]?.Value<decimal>() ?? 0,
                    OpenLevel = pos["position"]?["openLevel"]?.Value<decimal>() ?? 0,
                    CurrentLevel = pos["market"]?["bid"]?.Value<decimal>() ?? 0,
                    Currency = pos["position"]?["currency"]?.ToString(),
                    UnrealizedPnL = pos["position"]?["profit"]?.Value<decimal>() ?? 0
                });
            }

            return positions;
        }

        #endregion

        #region Orders

        /// <summary>
        /// Gets all working orders
        /// </summary>
        public List<IGWorkingOrder> GetWorkingOrders()
        {
            var response = SendRequest(HttpMethod.Get, IGApiEndpoints.WorkingOrders, allowNotFound: true);
            if (response == null) return new List<IGWorkingOrder>();
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            var orders = new List<IGWorkingOrder>();
            foreach (var wo in json["workingOrders"])
            {
                orders.Add(new IGWorkingOrder
                {
                    DealId = wo["workingOrderData"]?["dealId"]?.ToString(),
                    Epic = wo["marketData"]?["epic"]?.ToString(),
                    Direction = wo["workingOrderData"]?["direction"]?.ToString(),
                    Size = wo["workingOrderData"]?["size"]?.Value<decimal>() ?? 0,
                    Level = wo["workingOrderData"]?["level"]?.Value<decimal>() ?? 0,
                    OrderType = wo["workingOrderData"]?["type"]?.ToString(),
                    CreatedDate = wo["workingOrderData"]?["createdDate"]?.Value<DateTime>() ?? DateTime.UtcNow
                });
            }

            return orders;
        }

        /// <summary>
        /// Places a new order
        /// </summary>
        public IGOrderResponse PlaceOrder(IGPlaceOrderRequest request)
        {
            var response = SendRequest(HttpMethod.Post, IGApiEndpoints.PositionsOtc, request, version: 2);
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            return new IGOrderResponse
            {
                DealReference = json["dealReference"]?.ToString(),
                Success = true
            };
        }

        /// <summary>
        /// Updates an existing order
        /// </summary>
        public IGOrderResponse UpdateOrder(IGUpdateOrderRequest request)
        {
            var response = SendRequest(HttpMethod.Put,
                $"{IGApiEndpoints.WorkingOrdersOtc}/{request.DealId}", request, version: 2);
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            return new IGOrderResponse
            {
                DealReference = json["dealReference"]?.ToString(),
                Success = true
            };
        }

        /// <summary>
        /// Cancels an order
        /// </summary>
        public IGOrderResponse CancelOrder(string dealId)
        {
            var response = SendRequest(HttpMethod.Delete,
                $"{IGApiEndpoints.WorkingOrdersOtc}/{dealId}");
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            return new IGOrderResponse
            {
                DealReference = json["dealReference"]?.ToString(),
                Success = true
            };
        }

        /// <summary>
        /// Gets the deal confirmation for a deal reference
        /// </summary>
        public JObject GetDealConfirmation(string dealReference)
        {
            var response = SendRequest(HttpMethod.Get, $"{IGApiEndpoints.Confirms}/{dealReference}");
            var body = GetResponseBody(response);
            return JObject.Parse(body);
        }

        #endregion

        #region Markets

        /// <summary>
        /// Gets market details for a specific EPIC
        /// </summary>
        public JObject GetMarketDetails(string epic)
        {
            var response = SendRequest(HttpMethod.Get, $"{IGApiEndpoints.Markets}/{epic}", version: 3);
            var body = GetResponseBody(response);
            return JObject.Parse(body);
        }

        /// <summary>
        /// Searches for markets
        /// </summary>
        public List<IGMarket> SearchMarkets(string searchTerm)
        {
            var response = SendRequest(HttpMethod.Get,
                $"{IGApiEndpoints.MarketSearch}{Uri.EscapeDataString(searchTerm)}");
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            var markets = new List<IGMarket>();
            foreach (var mkt in json["markets"])
            {
                markets.Add(new IGMarket
                {
                    Epic = mkt["epic"]?.ToString(),
                    InstrumentName = mkt["instrumentName"]?.ToString(),
                    InstrumentType = mkt["instrumentType"]?.ToString()
                });
            }

            return markets;
        }

        #endregion

        #region Historical Data

        /// <summary>
        /// Gets historical prices
        /// </summary>
        public List<IGPriceCandle> GetHistoricalPrices(string epic, string resolution,
            DateTime startDate, DateTime endDate)
        {
            // TODO: Where can I find this REST endpoint?
            var url = $"{IGApiEndpoints.Prices}/{epic}?resolution={resolution}" +
                      $"&from={startDate:yyyy-MM-ddTHH:mm:ss}&to={endDate:yyyy-MM-ddTHH:mm:ss}";

            var response = SendRequest(HttpMethod.Get, url, version: 3);
            var body = GetResponseBody(response);
            var json = JObject.Parse(body);

            var prices = new List<IGPriceCandle>();
            foreach (var p in json["prices"])
            {
                prices.Add(new IGPriceCandle
                {
                    SnapshotTime = p["snapshotTime"]?.Value<DateTime>() ?? DateTime.UtcNow,
                    OpenBid = p["openPrice"]?["bid"]?.Value<decimal>() ?? 0,
                    HighBid = p["highPrice"]?["bid"]?.Value<decimal>() ?? 0,
                    LowBid = p["lowPrice"]?["bid"]?.Value<decimal>() ?? 0,
                    CloseBid = p["closePrice"]?["bid"]?.Value<decimal>() ?? 0,
                    OpenAsk = p["openPrice"]?["ask"]?.Value<decimal>() ?? 0,
                    HighAsk = p["highPrice"]?["ask"]?.Value<decimal>() ?? 0,
                    LowAsk = p["lowPrice"]?["ask"]?.Value<decimal>() ?? 0,
                    CloseAsk = p["closePrice"]?["ask"]?.Value<decimal>() ?? 0,
                    Volume = p["lastTradedVolume"]?.Value<long>()
                });
            }

            return prices;
        }

        #endregion

        #region HTTP Helpers

        private HttpResponseMessage SendRequest(HttpMethod method, string endpoint,
            object body = null, int version = 1, bool allowNotFound = false)
        {
            var request = new HttpRequestMessage(method, _baseUrl + endpoint);

            // Add version header
            request.Headers.Add("VERSION", version.ToString());

            // Add session tokens if available
            if (!string.IsNullOrEmpty(_cst))
            {
                request.Headers.Add("CST", _cst);
            }
            if (!string.IsNullOrEmpty(_securityToken))
            {
                request.Headers.Add("X-SECURITY-TOKEN", _securityToken);
            }

            // Add body if present
            if (body != null)
            {
                var json = JsonConvert.SerializeObject(body);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            var response = _httpClient.SendAsync(request).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                if (allowNotFound && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }
                var errorBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new Exception($"IG API Error: {response.StatusCode} - {errorBody}");
            }

            return response;
        }

        private string GetHeader(HttpResponseMessage response, string headerName)
        {
            if (response.Headers.TryGetValues(headerName, out var values))
            {
                return string.Join("", values);
            }
            return null;
        }

        private string GetResponseBody(HttpResponseMessage response)
        {
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
