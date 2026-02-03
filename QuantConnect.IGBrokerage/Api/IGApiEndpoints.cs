namespace QuantConnect.Brokerages.IG.Api
{
    /// <summary>
    /// IG Markets API endpoint constants
    /// </summary>
    public static class IGApiEndpoints
    {
        // Base URLs
        public const string DemoBaseUrl = "https://demo-api.ig.com/gateway/deal";
        public const string LiveBaseUrl = "https://api.ig.com/gateway/deal";

        // Session
        public const string Session = "/session";
        public const string SessionEncryption = "/session/encryptionKey";
        public const string RefreshToken = "/session/refresh-token";

        // Accounts
        public const string Accounts = "/accounts";
        public const string AccountSwitch = "/session";

        // Positions
        public const string Positions = "/positions";
        public const string PositionsOtc = "/positions/otc";

        // Working Orders
        public const string WorkingOrders = "/workingorders/otc";
        public const string WorkingOrdersOtc = "/workingorders/otc";

        // Markets
        public const string Markets = "/markets";
        public const string MarketSearch = "/markets?searchTerm=";
        public const string MarketNavigation = "/marketnavigation";

        // Prices
        public const string Prices = "/prices";

        // Watchlists
        public const string Watchlists = "/watchlists";

        // History
        public const string ActivityHistory = "/history/activity";
        public const string TransactionHistory = "/history/transactions";

        // Confirms
        public const string Confirms = "/confirms";

        // Client Sentiment
        public const string ClientSentiment = "/clientsentiment";
    }
}
