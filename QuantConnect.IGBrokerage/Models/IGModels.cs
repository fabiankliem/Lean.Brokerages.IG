using Newtonsoft.Json;
using System;

namespace QuantConnect.Brokerages.IG.Models
{
    // Request Models - IG API expects camelCase JSON property names
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

    // Response Models
    public class IGLoginResponse
    {
        public string Cst { get; set; }
        public string SecurityToken { get; set; }
        public string LightstreamerEndpoint { get; set; }
        public string AccountId { get; set; }
        public string ClientId { get; set; }
    }

    public class IGOrderResponse
    {
        public string DealReference { get; set; }
        public bool Success { get; set; }
        public string Reason { get; set; }
    }

    public class IGAccount
    {
        public string AccountId { get; set; }
        public string AccountName { get; set; }
        public string AccountType { get; set; }
        public string Currency { get; set; }
        public IGAccountBalance Balance { get; set; }
    }

    public class IGAccountBalance
    {
        public decimal Available { get; set; }
        public decimal Balance { get; set; }
        public decimal Deposit { get; set; }
        public decimal ProfitLoss { get; set; }
    }

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

    public class IGMarket
    {
        public string Epic { get; set; }
        public string InstrumentName { get; set; }
        public string InstrumentType { get; set; }
    }

    public class IGPriceCandle
    {
        public DateTime SnapshotTime { get; set; }
        public decimal OpenBid { get; set; }
        public decimal HighBid { get; set; }
        public decimal LowBid { get; set; }
        public decimal CloseBid { get; set; }
        public decimal OpenAsk { get; set; }
        public decimal HighAsk { get; set; }
        public decimal LowAsk { get; set; }
        public decimal CloseAsk { get; set; }
        public long? Volume { get; set; }
    }
}
