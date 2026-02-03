using System;

namespace QuantConnect.Brokerages.IG.Models
{
    // Request Models
    public class IGPlaceOrderRequest
    {
        public string Epic { get; set; }
        public string Direction { get; set; }
        public decimal Size { get; set; }
        public string OrderType { get; set; }
        public decimal? Level { get; set; }
        public decimal? LimitDistance { get; set; }
        public decimal? StopDistance { get; set; }
        public string TimeInForce { get; set; }
        public string GoodTillDate { get; set; }
        public string CurrencyCode { get; set; }
        public string Expiry { get; set; }
        public bool GuaranteedStop { get; set; }
        public bool ForceOpen { get; set; }
    }

    public class IGUpdateOrderRequest
    {
        public string DealId { get; set; }
        public decimal? Level { get; set; }
        public decimal? LimitDistance { get; set; }
        public decimal? StopDistance { get; set; }
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
