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
using System;
using System.Collections.Generic;

namespace QuantConnect.Brokerages.IG.Models
{
    #region Request Models

    /// <summary>
    /// Login request for IG session authentication
    /// </summary>
    public class IGLoginRequest
    {
        [JsonProperty("identifier")]
        public string Identifier { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }
    }

    /// <summary>
    /// Request to place a new order via IG API
    /// </summary>
    public class IGPlaceOrderRequest
    {
        [JsonProperty("epic")]
        public string Epic { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("size")]
        public decimal Size { get; set; }

        [JsonProperty("orderType")]
        public string OrderType { get; set; }

        [JsonProperty("level")]
        public decimal? Level { get; set; }

        [JsonProperty("limitDistance")]
        public decimal? LimitDistance { get; set; }

        [JsonProperty("stopDistance")]
        public decimal? StopDistance { get; set; }

        [JsonProperty("timeInForce")]
        public string TimeInForce { get; set; }

        [JsonProperty("goodTillDate")]
        public string GoodTillDate { get; set; }

        [JsonProperty("currencyCode")]
        public string CurrencyCode { get; set; }

        [JsonProperty("expiry")]
        public string Expiry { get; set; }

        [JsonProperty("guaranteedStop")]
        public bool GuaranteedStop { get; set; }

        [JsonProperty("forceOpen")]
        public bool ForceOpen { get; set; }
    }

    /// <summary>
    /// Request to update an existing working order
    /// </summary>
    public class IGUpdateOrderRequest
    {
        [JsonIgnore]
        public string DealId { get; set; }

        [JsonProperty("level")]
        public decimal? Level { get; set; }

        [JsonProperty("limitDistance")]
        public decimal? LimitDistance { get; set; }

        [JsonProperty("stopDistance")]
        public decimal? StopDistance { get; set; }

        [JsonProperty("goodTillDate")]
        public string GoodTillDate { get; set; }
    }

    #endregion

    #region API Response Wrappers (mirror IG JSON structure for typed deserialization)

    /// <summary>
    /// Wrapper for GET /session response
    /// </summary>
    public class IGSessionResponse
    {
        [JsonProperty("currentAccountId")]
        public string CurrentAccountId { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("lightstreamerEndpoint")]
        public string LightstreamerEndpoint { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /accounts response
    /// </summary>
    public class IGAccountsResponse
    {
        [JsonProperty("accounts")]
        public List<IGAccountData> Accounts { get; set; }
    }

    /// <summary>
    /// Account data from IG API
    /// </summary>
    public class IGAccountData
    {
        [JsonProperty("accountId")]
        public string AccountId { get; set; }

        [JsonProperty("accountName")]
        public string AccountName { get; set; }

        [JsonProperty("accountType")]
        public string AccountType { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; }

        [JsonProperty("balance")]
        public IGBalanceData Balance { get; set; }
    }

    /// <summary>
    /// Balance data nested in account response
    /// </summary>
    public class IGBalanceData
    {
        [JsonProperty("available")]
        public decimal Available { get; set; }

        [JsonProperty("balance")]
        public decimal Balance { get; set; }

        [JsonProperty("deposit")]
        public decimal Deposit { get; set; }

        [JsonProperty("profitLoss")]
        public decimal ProfitLoss { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /positions response
    /// </summary>
    public class IGPositionsResponse
    {
        [JsonProperty("positions")]
        public List<IGPositionWrapper> Positions { get; set; }
    }

    /// <summary>
    /// Position wrapper containing position and market data
    /// </summary>
    public class IGPositionWrapper
    {
        [JsonProperty("position")]
        public IGPositionData Position { get; set; }

        [JsonProperty("market")]
        public IGMarketData Market { get; set; }
    }

    /// <summary>
    /// Position data from IG API
    /// </summary>
    public class IGPositionData
    {
        [JsonProperty("dealId")]
        public string DealId { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("size")]
        public decimal Size { get; set; }

        [JsonProperty("openLevel")]
        public decimal OpenLevel { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; }

        [JsonProperty("profit")]
        public decimal Profit { get; set; }
    }

    /// <summary>
    /// Market data nested in position/order responses
    /// </summary>
    public class IGMarketData
    {
        [JsonProperty("epic")]
        public string Epic { get; set; }

        [JsonProperty("bid")]
        public decimal? Bid { get; set; }

        [JsonProperty("offer")]
        public decimal? Offer { get; set; }

        [JsonProperty("instrumentName")]
        public string InstrumentName { get; set; }

        [JsonProperty("instrumentType")]
        public string InstrumentType { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /workingorders response
    /// </summary>
    public class IGWorkingOrdersResponse
    {
        [JsonProperty("workingOrders")]
        public List<IGWorkingOrderWrapper> WorkingOrders { get; set; }
    }

    /// <summary>
    /// Working order wrapper containing order and market data
    /// </summary>
    public class IGWorkingOrderWrapper
    {
        [JsonProperty("workingOrderData")]
        public IGWorkingOrderData WorkingOrderData { get; set; }

        [JsonProperty("marketData")]
        public IGMarketData MarketData { get; set; }
    }

    /// <summary>
    /// Working order data from IG API
    /// </summary>
    public class IGWorkingOrderData
    {
        [JsonProperty("dealId")]
        public string DealId { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("size")]
        public decimal Size { get; set; }

        [JsonProperty("level")]
        public decimal Level { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("createdDate")]
        public DateTime CreatedDate { get; set; }
    }

    /// <summary>
    /// Wrapper for POST /positions/otc and PUT/DELETE /workingorders response
    /// </summary>
    public class IGDealReferenceResponse
    {
        [JsonProperty("dealReference")]
        public string DealReference { get; set; }
    }

    /// <summary>
    /// Deal confirmation from GET /confirms/{dealReference}
    /// </summary>
    public class IGDealConfirmation
    {
        [JsonProperty("dealId")]
        public string DealId { get; set; }

        [JsonProperty("dealReference")]
        public string DealReference { get; set; }

        [JsonProperty("dealStatus")]
        public string DealStatus { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("level")]
        public decimal Level { get; set; }

        [JsonProperty("size")]
        public decimal Size { get; set; }

        [JsonProperty("direction")]
        public string Direction { get; set; }

        [JsonProperty("epic")]
        public string Epic { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /markets?searchTerm= response
    /// </summary>
    public class IGMarketsSearchResponse
    {
        [JsonProperty("markets")]
        public List<IGMarketSearchResult> Markets { get; set; }
    }

    /// <summary>
    /// Market search result
    /// </summary>
    public class IGMarketSearchResult
    {
        [JsonProperty("epic")]
        public string Epic { get; set; }

        [JsonProperty("instrumentName")]
        public string InstrumentName { get; set; }

        [JsonProperty("instrumentType")]
        public string InstrumentType { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /markets/{epic} response
    /// </summary>
    public class IGMarketDetailsResponse
    {
        [JsonProperty("instrument")]
        public IGInstrumentDetails Instrument { get; set; }

        [JsonProperty("snapshot")]
        public IGSnapshotDetails Snapshot { get; set; }

        [JsonProperty("dealingRules")]
        public IGDealingRules DealingRules { get; set; }
    }

    /// <summary>
    /// Instrument details from market data
    /// </summary>
    public class IGInstrumentDetails
    {
        [JsonProperty("epic")]
        public string Epic { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("currencies")]
        public List<IGCurrencyInfo> Currencies { get; set; }

        [JsonProperty("onePipMeans")]
        public string OnePipMeans { get; set; }

        [JsonProperty("contractSize")]
        public string ContractSize { get; set; }

        [JsonProperty("lotSize")]
        public decimal? LotSize { get; set; }
    }

    /// <summary>
    /// Currency info from instrument details
    /// </summary>
    public class IGCurrencyInfo
    {
        [JsonProperty("code")]
        public string Code { get; set; }
    }

    /// <summary>
    /// Snapshot data from market details
    /// </summary>
    public class IGSnapshotDetails
    {
        [JsonProperty("bid")]
        public decimal? Bid { get; set; }

        [JsonProperty("offer")]
        public decimal? Offer { get; set; }

        [JsonProperty("marketStatus")]
        public string MarketStatus { get; set; }
    }

    /// <summary>
    /// Dealing rules from market details
    /// </summary>
    public class IGDealingRules
    {
        [JsonProperty("minDealSize")]
        public IGDealingRuleValue MinDealSize { get; set; }
    }

    /// <summary>
    /// Dealing rule value
    /// </summary>
    public class IGDealingRuleValue
    {
        [JsonProperty("value")]
        public decimal Value { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; }
    }

    /// <summary>
    /// Wrapper for GET /prices/{epic} response
    /// </summary>
    public class IGPricesResponse
    {
        [JsonProperty("prices")]
        public List<IGPriceCandleData> Prices { get; set; }
    }

    /// <summary>
    /// Price candle data from IG API
    /// </summary>
    public class IGPriceCandleData
    {
        [JsonProperty("snapshotTime")]
        public DateTime SnapshotTime { get; set; }

        [JsonProperty("openPrice")]
        public IGPricePoint OpenPrice { get; set; }

        [JsonProperty("highPrice")]
        public IGPricePoint HighPrice { get; set; }

        [JsonProperty("lowPrice")]
        public IGPricePoint LowPrice { get; set; }

        [JsonProperty("closePrice")]
        public IGPricePoint ClosePrice { get; set; }

        [JsonProperty("lastTradedVolume")]
        public long? LastTradedVolume { get; set; }
    }

    /// <summary>
    /// Bid/ask price point
    /// </summary>
    public class IGPricePoint
    {
        [JsonProperty("bid")]
        public decimal? Bid { get; set; }

        [JsonProperty("ask")]
        public decimal? Ask { get; set; }
    }

    #endregion

    #region Public Models (used by IGBrokerage consumers)

    /// <summary>
    /// Login response containing session tokens and connection info
    /// </summary>
    public class IGLoginResponse
    {
        public string LightstreamerEndpoint { get; set; }
        public string AccountId { get; set; }
        public string ClientId { get; set; }
    }

    /// <summary>
    /// Flattened position data for brokerage use
    /// </summary>
    public class IGPosition
    {
        public string DealId { get; set; }
        public string Epic { get; set; }
        public string Direction { get; set; }
        public decimal Size { get; set; }
        public decimal OpenLevel { get; set; }
        public decimal CurrentLevel { get; set; }
        public string Currency { get; set; }
        public decimal UnrealizedPnL { get; set; }
    }

    /// <summary>
    /// Flattened working order data for brokerage use
    /// </summary>
    public class IGWorkingOrder
    {
        public string DealId { get; set; }
        public string Epic { get; set; }
        public string Direction { get; set; }
        public decimal Size { get; set; }
        public decimal Level { get; set; }
        public string OrderType { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    #endregion
}
