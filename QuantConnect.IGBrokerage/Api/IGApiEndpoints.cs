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

        // Accounts
        public const string Accounts = "/accounts";

        // Positions
        public const string Positions = "/positions";
        public const string PositionsOtc = "/positions/otc";

        // Working Orders (per IG docs: https://labs.ig.com/reference/working-orders-otc.html)
        public const string WorkingOrders = "/workingorders/otc";

        // Markets
        public const string Markets = "/markets";
        public const string MarketSearch = "/markets?searchTerm=";

        // Prices
        public const string Prices = "/prices";

        // Confirms
        public const string Confirms = "/confirms";
    }
}
