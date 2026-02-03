# Critical Gaps Implementation Plan
## IG Markets Brokerage - README Compliance

**Document Version:** 1.0
**Date:** 2026-02-03
**Priority:** P0 - Critical (Must complete before production)

---

## Overview

This plan addresses the 5 critical gaps (P0) and 3 high-priority items (P1) identified in the README.md compliance validation. Each item includes detailed implementation steps, code examples, and testing requirements.

---

## P0: CRITICAL GAPS (Must Fix)

### 🔴 Gap 1: Fix gh-actions.yml - Template References

**File:** `/.github/workflows/gh-actions.yml`
**Impact:** CI/CD pipeline completely broken
**Estimated Time:** 15 minutes

#### Current Issues
```yaml
Line 44: Lean.Brokerages.Template (path)
Line 44: QC_TEMPLATE_BROKERAGE_KEY, QC_TEMPLATE_BROKERAGE_SECRET (env vars)
Line 48: QuantConnect.TemplateBrokerage.sln (solution file)
Line 50: QuantConnect.TemplateBrokerage.Tests (test assembly)
```

#### Implementation Steps

1. **Update Docker options (Line 44)**
```yaml
# BEFORE:
options: --workdir /__w/Lean.Brokerages.Template/Lean.Brokerages.Template -v /home/runner/work:/__w -e QC_TEMPLATE_BROKERAGE_KEY=${{ secrets.QC_TEMPLATE_BROKERAGE_KEY }} -e QC_TEMPLATE_BROKERAGE_SECRET=${{ secrets.QC_TEMPLATE_BROKERAGE_SECRET }}

# AFTER:
options: --workdir /__w/Lean.Brokerages.IG/Lean.Brokerages.IG -v /home/runner/work:/__w -e QC_IG_API_KEY=${{ secrets.QC_IG_API_KEY }} -e QC_IG_IDENTIFIER=${{ secrets.QC_IG_IDENTIFIER }} -e QC_IG_PASSWORD=${{ secrets.QC_IG_PASSWORD }} -e QC_IG_ACCOUNT_ID=${{ secrets.QC_IG_ACCOUNT_ID }}
```

2. **Update build command (Line 48)**
```yaml
# BEFORE:
dotnet build /p:Configuration=Release /v:quiet /p:WarningLevel=1 QuantConnect.TemplateBrokerage.sln

# AFTER:
dotnet build /p:Configuration=Release /v:quiet /p:WarningLevel=1 QuantConnect.IGBrokerage.sln
```

3. **Update test command (Line 50)**
```yaml
# BEFORE:
dotnet test ./QuantConnect.TemplateBrokerage.Tests/bin/Release/QuantConnect.Brokerages.Template.Tests.dll

# AFTER:
dotnet test ./QuantConnect.IGBrokerage.Tests/bin/Release/QuantConnect.IGBrokerage.Tests.dll
```

4. **Add GitHub Secrets** (via GitHub repo settings)
   - `QC_IG_API_KEY`
   - `QC_IG_IDENTIFIER`
   - `QC_IG_PASSWORD`
   - `QC_IG_ACCOUNT_ID`

#### Verification
```bash
# Trigger workflow and verify it runs successfully
git push origin master

# Check Actions tab in GitHub for green build
```

---

### 🔴 Gap 2: Complete config.json - Credential Fields

**File:** `/QuantConnect.IGBrokerage.Tests/config.json`
**Impact:** Cannot run any tests locally
**Estimated Time:** 10 minutes

#### Current State
```json
{
  "data-folder": "../../../../Lean/Data/"
}
```

#### Implementation

**Step 1:** Update config.json with all required fields
```json
{
  "data-folder": "../../../../Lean/Data/",
  "ig-api-url": "https://demo-api.ig.com/gateway/deal",
  "ig-api-key": "",
  "ig-identifier": "",
  "ig-password": "",
  "ig-account-id": "",
  "ig-environment": "demo"
}
```

**Step 2:** Create config.example.json (for version control)
```json
{
  "data-folder": "../../../../Lean/Data/",
  "ig-api-url": "https://demo-api.ig.com/gateway/deal",
  "ig-api-key": "your-api-key-here",
  "ig-identifier": "your-username-here",
  "ig-password": "your-password-here",
  "ig-account-id": "your-account-id-here",
  "ig-environment": "demo",

  "// NOTE": "Copy this to config.json and fill in your credentials",
  "// DEMO": "Use demo-api.ig.com for testing",
  "// LIVE": "Use api.ig.com for production"
}
```

**Step 3:** Update .gitignore
```gitignore
# Add to .gitignore if not already present
**/config.json
!**/config.example.json
```

**Step 4:** Add README section about configuration
```markdown
## Testing Configuration

1. Copy `config.example.json` to `config.json` in the test project
2. Fill in your IG Markets credentials:
   - Get API key from: https://labs.ig.com/
   - Use demo credentials for testing
3. Never commit config.json with real credentials
```

#### Verification
```bash
# Verify config is read correctly
dotnet test QuantConnect.IGBrokerage.Tests --filter "FullyQualifiedName~IGBrokerageFactoryTests"
```

---

### 🔴 Gap 3: Implement ValidateSubscription()

**File:** `QuantConnect.IGBrokerage/IGBrokerage.cs`
**Impact:** May allow invalid subscriptions, potential runtime errors
**Estimated Time:** 30 minutes

#### Implementation

**Step 1:** Add ValidateSubscription() method after Initialize()

```csharp
/// <summary>
/// Validates that the brokerage supports the requested subscription
/// Called during initialization to catch configuration errors early
/// </summary>
/// <param name="symbol">Symbol to validate</param>
/// <param name="securityType">Security type</param>
/// <param name="resolution">Data resolution</param>
/// <param name="tickType">Tick type</param>
/// <returns>True if subscription is valid</returns>
private bool ValidateSubscription(Symbol symbol, SecurityType securityType, Resolution resolution, TickType tickType)
{
    // Validate security type is supported
    if (!_supportedSecurityTypes.Contains(securityType))
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedSecurityType",
            $"IG Markets does not support {securityType}. Supported types: {string.Join(", ", _supportedSecurityTypes)}"));
        return false;
    }

    // Validate symbol can be mapped
    var epic = _symbolMapper.GetBrokerageSymbol(symbol);
    if (string.IsNullOrEmpty(epic))
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnmappedSymbol",
            $"Symbol {symbol} cannot be mapped to IG EPIC. Use symbol mapper or SearchMarkets API."));
        return false;
    }

    // Validate resolution is supported for history
    if (resolution == Resolution.Tick)
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedResolution",
            $"IG Markets does not support {resolution} historical data. Use Second or higher."));
        return false;
    }

    // Validate tick type combinations
    if (tickType == TickType.OpenInterest)
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedTickType",
            $"IG Markets does not support OpenInterest data."));
        return false;
    }

    // Forex and CFD typically support both quote and trade
    // Indices typically only support trade
    if (securityType == SecurityType.Index && tickType == TickType.Quote)
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "UnsupportedTickType",
            $"Index {symbol} may not support quote data. Try TickType.Trade."));
    }

    return true;
}

/// <summary>
/// Validates that all current subscriptions are supported by the brokerage
/// Should be called during initialization
/// </summary>
private void ValidateSubscriptions()
{
    if (_algorithm == null)
        return;

    Log.Trace("IGBrokerage.ValidateSubscriptions(): Validating algorithm subscriptions...");

    var subscriptions = _algorithm.SubscriptionManager.Subscriptions;
    var invalidCount = 0;

    foreach (var subscription in subscriptions)
    {
        var symbol = subscription.Symbol;
        var config = subscription.Configuration;

        if (!ValidateSubscription(symbol, config.SecurityType, config.Resolution, config.TickType))
        {
            invalidCount++;
        }
    }

    if (invalidCount > 0)
    {
        Log.Trace($"IGBrokerage.ValidateSubscriptions(): Found {invalidCount} potentially invalid subscriptions");
    }
    else
    {
        Log.Trace("IGBrokerage.ValidateSubscriptions(): All subscriptions validated successfully");
    }
}
```

**Step 2:** Add supported types constant at class level
```csharp
// Add near the top of IGBrokerage class with other private fields
private static readonly HashSet<SecurityType> _supportedSecurityTypes = new HashSet<SecurityType>
{
    SecurityType.Forex,
    SecurityType.Index,
    SecurityType.Cfd,
    SecurityType.Crypto,
    SecurityType.Equity
};
```

**Step 3:** Call ValidateSubscriptions() in Initialize()
```csharp
// In Initialize() method, add after authentication but before return
// Around line 260 after "Log.Trace("IGBrokerage.Initialize(): Initialization complete");"

ValidateSubscriptions();
```

**Step 4:** Update CanSubscribe() to use the same validation
```csharp
// Existing CanSubscribe() method (lines 861-876) - update to:
private bool CanSubscribe(Symbol symbol)
{
    if (symbol.IsCanonical())
    {
        Log.Trace($"IGBrokerage.CanSubscribe(): Cannot subscribe to canonical symbol: {symbol}");
        return false;
    }

    // Use centralized validation
    return ValidateSubscription(symbol, symbol.SecurityType, Resolution.Minute, TickType.Trade);
}
```

#### Verification

**Unit Test:** Add to `IGBrokerageAdditionalTests.cs`
```csharp
[Test]
public void ValidateSubscription_SupportedSymbol_ReturnsTrue()
{
    var brokerage = new IGBrokerage();
    var method = GetPrivateMethod(brokerage, "ValidateSubscription");
    var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);

    var result = (bool)method.Invoke(brokerage,
        new object[] { symbol, SecurityType.Forex, Resolution.Minute, TickType.Quote });

    Assert.IsTrue(result);
}

[Test]
public void ValidateSubscription_UnsupportedSecurityType_ReturnsFalse()
{
    var brokerage = new IGBrokerage();
    var method = GetPrivateMethod(brokerage, "ValidateSubscription");
    var symbol = Symbol.Create("SPY", SecurityType.Future, Market.IG);

    var result = (bool)method.Invoke(brokerage,
        new object[] { symbol, SecurityType.Future, Resolution.Minute, TickType.Trade });

    Assert.IsFalse(result);
}
```

---

### 🔴 Gap 4: Implement IGBrokerageTests.cs Base Methods

**File:** `/QuantConnect.IGBrokerage.Tests/IGBrokerageTests.cs`
**Impact:** No integration test coverage for core functionality
**Estimated Time:** 2 hours

#### Current Issues
- Class marked `[Ignore("Not implemented")]`
- Three methods throw `NotImplementedException`
- Inherits from `BrokerageTests` but doesn't implement required methods

#### Implementation

**Step 1:** Implement CreateBrokerage()
```csharp
protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
{
    // Get configuration from config.json
    var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
    var apiKey = Config.Get("ig-api-key");
    var identifier = Config.Get("ig-identifier");
    var password = Config.Get("ig-password");
    var accountId = Config.Get("ig-account-id");
    var environment = Config.Get("ig-environment", "demo");

    if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
    {
        Assert.Ignore("IGBrokerageTests: Credentials not configured in config.json");
    }

    // Create brokerage instance
    var brokerage = new IGBrokerage(
        apiUrl,
        apiKey,
        identifier,
        password,
        accountId,
        environment,
        orderProvider,
        securityProvider
    );

    // Connect to IG
    brokerage.Connect();

    // Wait for connection
    Thread.Sleep(2000);

    if (!brokerage.IsConnected)
    {
        Assert.Fail("IGBrokerage failed to connect");
    }

    return brokerage;
}
```

**Step 2:** Implement IsAsync()
```csharp
protected override bool IsAsync()
{
    // IG uses Lightstreamer for real-time updates
    // Order events come asynchronously via WebSocket
    return true;
}
```

**Step 3:** Implement GetAskPrice()
```csharp
protected override decimal GetAskPrice(Symbol symbol)
{
    // Get current market data for the symbol
    var brokerage = (IGBrokerage)Brokerage;
    var epic = brokerage.SymbolMapper.GetBrokerageSymbol(symbol);

    if (string.IsNullOrEmpty(epic))
    {
        Assert.Fail($"Cannot map symbol {symbol} to IG EPIC");
    }

    try
    {
        // Use IG REST API to get current prices
        var marketData = brokerage.GetMarketData(epic);

        // Return offer (ask) price
        return marketData.Offer;
    }
    catch (Exception ex)
    {
        Assert.Fail($"Failed to get ask price for {symbol}: {ex.Message}");
        return 0; // Never reached
    }
}
```

**Step 4:** Add GetMarketData helper method to IGBrokerage.cs
```csharp
// Add to IGBrokerage.cs as internal method for testing
internal dynamic GetMarketData(string epic)
{
    _nonTradingRateGate.WaitToProceed();

    var endpoint = $"/markets/{epic}";
    var response = _restClient.Get(endpoint);

    return new
    {
        Bid = (decimal)response.snapshot.bid,
        Offer = (decimal)response.snapshot.offer,
        High = (decimal)response.snapshot.high,
        Low = (decimal)response.snapshot.low
    };
}
```

**Step 5:** Implement Symbol and SecurityType properties
```csharp
// At class level, update the properties:
protected override Symbol Symbol => Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
protected override SecurityType SecurityType => SecurityType.Forex;
```

**Step 6:** Remove [Ignore] attribute and update OrderParameters
```csharp
// Change line 26 from:
[TestFixture, Ignore("Not implemented")]

// To:
[TestFixture, Explicit("Requires IG Markets credentials")]

// Update OrderParameters() to use IG-specific symbols:
private static IEnumerable<TestCaseData> OrderParameters()
{
    // Use forex pairs that IG definitely supports
    yield return new TestCaseData(new MarketOrderTestParameters(
        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG)));

    yield return new TestCaseData(new LimitOrderTestParameters(
        Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
        1.1000m,  // High price
        1.0500m   // Low price
    ));

    yield return new TestCaseData(new StopMarketOrderTestParameters(
        Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG),
        1.3000m,  // High price
        1.2500m   // Low price
    ));

    // IG supports indices
    yield return new TestCaseData(new MarketOrderTestParameters(
        Symbol.Create("SPX", SecurityType.Index, Market.IG)));

    // Note: Remove options for now as IG option support is limited
}
```

**Step 7:** Add Setup and TearDown methods
```csharp
[SetUp]
public void Setup()
{
    Log.DebuggingEnabled = true;
    Log.DebuggingLevel = 1;
}

[TearDown]
public void TearDown()
{
    if (Brokerage != null && Brokerage.IsConnected)
    {
        Brokerage.Disconnect();
        Brokerage.Dispose();
    }
}
```

#### Verification
```bash
# Run specific test to verify implementation
dotnet test --filter "FullyQualifiedName~IGBrokerageTests.LongFromZero"

# If test completes without NotImplementedException, it's working
```

---

### 🔴 Gap 5: Remove [Ignore] from History Tests

**File:** `/QuantConnect.IGBrokerage.Tests/IGBrokerageHistoryProviderTests.cs`
**Impact:** History functionality not validated
**Estimated Time:** 30 minutes

#### Implementation

**Step 1:** Update test attribute
```csharp
// Line 28: Change from:
[TestFixture, Ignore("Not implemented")]

// To:
[TestFixture, Explicit("Requires IG Markets credentials")]
```

**Step 2:** Update test symbols to IG-specific ones
```csharp
// In TestParameters property, update test cases to use IG symbols:
private static TestCaseData[] TestParameters
{
    get
    {
        TestGlobals.Initialize();

        return
        [
            // Forex - IG's primary market
            new TestCaseData(
                Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                Resolution.Minute,
                TimeSpan.FromHours(2),
                TickType.Quote,
                typeof(QuoteBar),
                false),

            new TestCaseData(
                Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                Resolution.Hour,
                TimeSpan.FromDays(5),
                TickType.Quote,
                typeof(QuoteBar),
                false),

            new TestCaseData(
                Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                Resolution.Daily,
                TimeSpan.FromDays(30),
                TickType.Quote,
                typeof(QuoteBar),
                false),

            // Index
            new TestCaseData(
                Symbol.Create("SPX", SecurityType.Index, Market.IG),
                Resolution.Hour,
                TimeSpan.FromDays(3),
                TickType.Trade,
                typeof(TradeBar),
                false),

            new TestCaseData(
                Symbol.Create("FTSE", SecurityType.Index, Market.IG),
                Resolution.Daily,
                TimeSpan.FromDays(30),
                TickType.Trade,
                typeof(TradeBar),
                false),

            // Invalid test - Tick not supported
            new TestCaseData(
                Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
                Resolution.Tick,
                TimeSpan.FromMinutes(5),
                TickType.Quote,
                typeof(Tick),
                true),  // Should fail

            // Invalid test - Wrong market
            new TestCaseData(
                Symbols.SPY,  // NYSE market, not IG
                Resolution.Daily,
                TimeSpan.FromDays(10),
                TickType.Trade,
                typeof(TradeBar),
                true),  // Should fail
        ];
    }
}
```

**Step 3:** Update GetsHistory test initialization
```csharp
public void GetsHistory(Symbol symbol, Resolution resolution, TimeSpan period,
    TickType tickType, Type dataType, bool invalidRequest)
{
    // Create brokerage with proper initialization
    var apiUrl = Config.Get("ig-api-url", "https://demo-api.ig.com/gateway/deal");
    var apiKey = Config.Get("ig-api-key");
    var identifier = Config.Get("ig-identifier");
    var password = Config.Get("ig-password");
    var accountId = Config.Get("ig-account-id");
    var environment = Config.Get("ig-environment", "demo");

    if (string.IsNullOrEmpty(apiKey))
    {
        Assert.Ignore("IG credentials not configured");
    }

    var brokerage = new IGBrokerage(
        apiUrl, apiKey, identifier, password, accountId, environment);

    brokerage.Connect();
    Thread.Sleep(1000); // Wait for connection

    var historyProvider = new BrokerageHistoryProvider();
    historyProvider.SetBrokerage(brokerage);
    historyProvider.Initialize(new HistoryProviderInitializeParameters(
        null, null, null, null, null, null, null, false, null, null, new AlgorithmSettings()));

    // Rest of test remains the same...
    var marketHoursDatabase = MarketHoursDatabase.FromDataFolder();
    var now = DateTime.UtcNow;

    var requests = new[]
    {
        new HistoryRequest(
            now.Add(-period),
            now,
            dataType,
            symbol,
            resolution,
            marketHoursDatabase.GetExchangeHours(symbol.ID.Market, symbol, symbol.SecurityType),
            marketHoursDatabase.GetDataTimeZone(symbol.ID.Market, symbol, symbol.SecurityType),
            resolution,
            false,
            false,
            DataNormalizationMode.Adjusted,
            tickType)
    };

    var historyArray = historyProvider.GetHistory(requests, TimeZones.Utc)?.ToArray();

    if (invalidRequest)
    {
        Assert.IsNull(historyArray);
        return;
    }

    Assert.IsNotNull(historyArray);
    // ... rest of assertions

    // Cleanup
    brokerage.Disconnect();
    brokerage.Dispose();
}
```

#### Verification
```bash
# Run history tests
dotnet test --filter "FullyQualifiedName~IGBrokerageHistoryProviderTests"

# Should see tests pass for valid cases, fail gracefully for invalid
```

---

## P1: HIGH PRIORITY ITEMS

### 🟠 Item 6: Refactor to use BrokerageConcurrentMessageHandler

**Files:** `IGBrokerage.cs`
**Impact:** Potential race conditions in order handling
**Estimated Time:** 3 hours

#### Current State
- Manual synchronization using `ConcurrentDictionary<int, Order>`
- Lock statements scattered throughout order handling
- No centralized message queue for order operations

#### Implementation

**Step 1:** Add BrokerageConcurrentMessageHandler field
```csharp
// Add to IGBrokerage.cs class fields (around line 100)
private readonly BrokerageConcurrentMessageHandler<PlaceOrderRequest> _messageHandler;

// Define request type
private class PlaceOrderRequest
{
    public Order Order { get; set; }
    public TaskCompletionSource<bool> CompletionSource { get; set; }
}
```

**Step 2:** Initialize in constructor
```csharp
// In IGBrokerage constructor (around line 150)
_messageHandler = new BrokerageConcurrentMessageHandler<PlaceOrderRequest>(ProcessOrderRequest);
```

**Step 3:** Create ProcessOrderRequest method
```csharp
/// <summary>
/// Processes order requests sequentially to avoid race conditions
/// Called by BrokerageConcurrentMessageHandler
/// </summary>
private void ProcessOrderRequest(PlaceOrderRequest request)
{
    try
    {
        var order = request.Order;

        // Use Monitor to synchronize with WebSocket updates
        lock (_orderUpdateLock)
        {
            // Place order via REST API
            var success = PlaceOrderInternal(order);

            // Set result
            request.CompletionSource.SetResult(success);
        }
    }
    catch (Exception ex)
    {
        Log.Error($"IGBrokerage.ProcessOrderRequest(): Error processing order: {ex.Message}");
        request.CompletionSource.SetException(ex);
    }
}
```

**Step 4:** Refactor PlaceOrder to use message handler
```csharp
public override bool PlaceOrder(Order order)
{
    if (!IsConnected)
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "NotConnected",
            "Cannot place order when not connected to IG Markets"));
        return false;
    }

    // Create request with completion source
    var request = new PlaceOrderRequest
    {
        Order = order,
        CompletionSource = new TaskCompletionSource<bool>()
    };

    // Queue request to message handler
    _messageHandler.HandleNewMessage(request);

    // Wait for result (with timeout)
    if (request.CompletionSource.Task.Wait(TimeSpan.FromSeconds(30)))
    {
        return request.CompletionSource.Task.Result;
    }
    else
    {
        OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Warning, "Timeout",
            $"Timeout waiting for order {order.Id} to be placed"));
        return false;
    }
}
```

**Step 5:** Extract PlaceOrderInternal() method
```csharp
/// <summary>
/// Internal method that actually places the order
/// Called by ProcessOrderRequest in synchronized context
/// </summary>
private bool PlaceOrderInternal(Order order)
{
    // Existing PlaceOrder logic moves here
    // Lines 444-591 from current IGBrokerage.cs

    // Rate limiting
    _tradingRateGate.WaitToProceed();

    // Map symbol to EPIC
    var epic = _symbolMapper.GetBrokerageSymbol(order.Symbol);

    // ... rest of implementation

    return true;
}
```

**Step 6:** Apply same pattern to UpdateOrder and CancelOrder
```csharp
// Create similar request types for Update and Cancel
private class UpdateOrderRequest
{
    public Order Order { get; set; }
    public TaskCompletionSource<bool> CompletionSource { get; set; }
}

private class CancelOrderRequest
{
    public Order Order { get; set; }
    public TaskCompletionSource<bool> CompletionSource { get; set; }
}

// Create separate handlers or use same handler with base request class
```

**Step 7:** Dispose handler in Dispose()
```csharp
public override void Dispose()
{
    base.Dispose();

    _messageHandler?.Dispose();
    _restClient?.Dispose();
    // ... rest of disposal
}
```

#### Testing
```csharp
[Test]
public void ConcurrentOrders_NoRaceCondition()
{
    // Place multiple orders concurrently
    var tasks = new List<Task<bool>>();

    for (int i = 0; i < 10; i++)
    {
        var order = new MarketOrder(
            Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
            1000,
            DateTime.UtcNow);

        tasks.Add(Task.Run(() => Brokerage.PlaceOrder(order)));
    }

    Task.WaitAll(tasks.ToArray());

    // All should succeed or fail gracefully
    Assert.IsTrue(tasks.All(t => t.IsCompleted));
}
```

---

### 🟠 Item 7: Add Unit Tests for Core Methods

**Estimated Time:** 4 hours

#### Implementation Plan

Create new test file: `IGBrokerageCoreMethodsTests.cs`

```csharp
using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Configuration;
using QuantConnect.Brokerages.IG;

namespace QuantConnect.Brokerages.IG.Tests
{
    [TestFixture, Explicit("Requires IG Markets credentials")]
    public class IGBrokerageCoreMethodsTests
    {
        private IGBrokerage _brokerage;

        [SetUp]
        public void Setup()
        {
            var apiUrl = Config.Get("ig-api-url");
            var apiKey = Config.Get("ig-api-key");
            var identifier = Config.Get("ig-identifier");
            var password = Config.Get("ig-password");
            var accountId = Config.Get("ig-account-id");
            var environment = Config.Get("ig-environment", "demo");

            if (string.IsNullOrEmpty(apiKey))
            {
                Assert.Ignore("IG credentials not configured");
            }

            _brokerage = new IGBrokerage(apiUrl, apiKey, identifier, password,
                accountId, environment);
            _brokerage.Connect();
            Thread.Sleep(2000);
        }

        [TearDown]
        public void TearDown()
        {
            _brokerage?.Disconnect();
            _brokerage?.Dispose();
        }

        #region GetCashBalance Tests

        [Test]
        public void GetCashBalance_ReturnsValidBalance()
        {
            // Act
            var balances = _brokerage.GetCashBalance().ToList();

            // Assert
            Assert.IsNotNull(balances);
            Assert.IsNotEmpty(balances);

            Console.WriteLine("Cash Balances:");
            foreach (var balance in balances)
            {
                Console.WriteLine($"  {balance.Currency}: {balance.Amount}");
                Assert.IsNotNull(balance.Currency);
                Assert.Greater(balance.Amount, 0); // Demo accounts should have positive balance
            }
        }

        [Test]
        public void GetCashBalance_ContainsBaseCurrency()
        {
            // Act
            var balances = _brokerage.GetCashBalance().ToList();

            // Assert
            var hasCurrency = balances.Any(b =>
                b.Currency == "GBP" ||
                b.Currency == "USD" ||
                b.Currency == "EUR");

            Assert.IsTrue(hasCurrency, "Should contain at least one major currency");
        }

        #endregion

        #region GetAccountHoldings Tests

        [Test]
        public void GetAccountHoldings_ReturnsListWithoutError()
        {
            // Act
            var holdings = _brokerage.GetAccountHoldings().ToList();

            // Assert
            Assert.IsNotNull(holdings);
            Console.WriteLine($"Current Holdings: {holdings.Count}");

            foreach (var holding in holdings)
            {
                Console.WriteLine($"  {holding.Symbol}: {holding.Quantity} @ {holding.AveragePrice}");
                Assert.IsNotNull(holding.Symbol);
                Assert.AreNotEqual(0, holding.Quantity);
            }
        }

        [Test]
        public void GetAccountHoldings_EmptyAccount_ReturnsEmptyList()
        {
            // This test assumes demo account starts empty
            // If not, place and close orders first

            // Act
            var holdings = _brokerage.GetAccountHoldings().ToList();

            // Assert - Should not throw, even if empty
            Assert.IsNotNull(holdings);
        }

        #endregion

        #region PlaceOrder Tests

        [Test]
        public void PlaceOrder_MarketOrder_Succeeds()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow)
            {
                Id = 1
            };

            bool orderFilled = false;
            _brokerage.OrdersStatusChanged += (sender, events) =>
            {
                foreach (var evt in events)
                {
                    if (evt.Status == Orders.OrderStatus.Filled)
                    {
                        orderFilled = true;
                    }
                }
            };

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Wait for fill
            Thread.Sleep(5000);

            // Assert
            Assert.IsTrue(result, "PlaceOrder should return true");
            Assert.IsTrue(orderFilled, "Order should be filled");

            // Cleanup - close position
            var closeOrder = new MarketOrder(symbol, -1000, DateTime.UtcNow) { Id = 2 };
            _brokerage.PlaceOrder(closeOrder);
            Thread.Sleep(2000);
        }

        [Test]
        public void PlaceOrder_LimitOrder_Succeeds()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var limitPrice = 0.9000m; // Very low, won't fill immediately
            var order = new LimitOrder(symbol, 1000, limitPrice, DateTime.UtcNow)
            {
                Id = 3
            };

            // Act
            var result = _brokerage.PlaceOrder(order);
            Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(result);

            // Cleanup
            _brokerage.CancelOrder(order);
            Thread.Sleep(1000);
        }

        [Test]
        public void PlaceOrder_StopMarketOrder_Succeeds()
        {
            // Arrange
            var symbol = Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG);
            var stopPrice = 2.0000m; // Very high, won't trigger immediately
            var order = new StopMarketOrder(symbol, 1000, stopPrice, DateTime.UtcNow)
            {
                Id = 4
            };

            // Act
            var result = _brokerage.PlaceOrder(order);
            Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(result);

            // Cleanup
            _brokerage.CancelOrder(order);
            Thread.Sleep(1000);
        }

        [Test]
        public void PlaceOrder_WithStopLoss_IncludesStopLoss()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow)
            {
                Id = 5,
                Tag = "SL:1.0500" // Stop loss at 1.0500
            };

            // Act
            var result = _brokerage.PlaceOrder(order);
            Thread.Sleep(5000);

            // Assert
            Assert.IsTrue(result);
            // Verify stop loss was attached by checking open orders

            // Cleanup
            var closeOrder = new MarketOrder(symbol, -1000, DateTime.UtcNow) { Id = 6 };
            _brokerage.PlaceOrder(closeOrder);
            Thread.Sleep(2000);
        }

        [Test]
        public void PlaceOrder_InvalidSymbol_Fails()
        {
            // Arrange
            var symbol = Symbol.Create("INVALID", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow)
            {
                Id = 7
            };

            // Act
            var result = _brokerage.PlaceOrder(order);

            // Assert
            Assert.IsFalse(result, "Should fail for invalid symbol");
        }

        #endregion

        #region UpdateOrder Tests

        [Test]
        public void UpdateOrder_LimitPrice_Succeeds()
        {
            // Arrange - Place initial order
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 0.9000m, DateTime.UtcNow)
            {
                Id = 8
            };

            _brokerage.PlaceOrder(order);
            Thread.Sleep(2000);

            // Act - Update limit price
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields { LimitPrice = 0.8500m }
            );

            var result = _brokerage.UpdateOrder(updateRequest);

            // Assert
            Assert.IsTrue(result);

            // Cleanup
            _brokerage.CancelOrder(order);
            Thread.Sleep(1000);
        }

        [Test]
        public void UpdateOrder_Quantity_Succeeds()
        {
            // Arrange
            var symbol = Symbol.Create("GBPUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 1.1000m, DateTime.UtcNow)
            {
                Id = 9
            };

            _brokerage.PlaceOrder(order);
            Thread.Sleep(2000);

            // Act
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                order.Id,
                new UpdateOrderFields { Quantity = 2000 }
            );

            var result = _brokerage.UpdateOrder(updateRequest);

            // Assert
            Assert.IsTrue(result);

            // Cleanup
            _brokerage.CancelOrder(order);
            Thread.Sleep(1000);
        }

        [Test]
        public void UpdateOrder_NonExistentOrder_Fails()
        {
            // Arrange
            var updateRequest = new UpdateOrderRequest(
                DateTime.UtcNow,
                99999, // Non-existent order ID
                new UpdateOrderFields { LimitPrice = 1.0000m }
            );

            // Act
            var result = _brokerage.UpdateOrder(updateRequest);

            // Assert
            Assert.IsFalse(result);
        }

        #endregion

        #region CancelOrder Tests

        [Test]
        public void CancelOrder_PendingOrder_Succeeds()
        {
            // Arrange - Place order that won't fill
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new LimitOrder(symbol, 1000, 0.9000m, DateTime.UtcNow)
            {
                Id = 10
            };

            _brokerage.PlaceOrder(order);
            Thread.Sleep(2000);

            bool orderCancelled = false;
            _brokerage.OrdersStatusChanged += (sender, events) =>
            {
                foreach (var evt in events)
                {
                    if (evt.Status == Orders.OrderStatus.Canceled)
                    {
                        orderCancelled = true;
                    }
                }
            };

            // Act
            var result = _brokerage.CancelOrder(order);
            Thread.Sleep(2000);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(orderCancelled);
        }

        [Test]
        public void CancelOrder_NonExistentOrder_Fails()
        {
            // Arrange
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow)
            {
                Id = 99999,
                BrokerId = new List<string> { "INVALID_BROKER_ID" }
            };

            // Act
            var result = _brokerage.CancelOrder(order);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public void CancelOrder_FilledOrder_Fails()
        {
            // Arrange - Place and wait for fill
            var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);
            var order = new MarketOrder(symbol, 1000, DateTime.UtcNow)
            {
                Id = 11
            };

            _brokerage.PlaceOrder(order);
            Thread.Sleep(5000); // Wait for fill

            // Act - Try to cancel filled order
            var result = _brokerage.CancelOrder(order);

            // Assert
            Assert.IsFalse(result, "Cannot cancel filled order");

            // Cleanup
            var closeOrder = new MarketOrder(symbol, -1000, DateTime.UtcNow) { Id = 12 };
            _brokerage.PlaceOrder(closeOrder);
            Thread.Sleep(2000);
        }

        #endregion
    }
}
```

---

### 🟠 Item 8: Implement SetJob() Logic

**File:** `IGBrokerage.cs` lines 747-751
**Impact:** May affect live trading initialization
**Estimated Time:** 1 hour

#### Implementation

```csharp
/// <summary>
/// Sets the job for this data queue handler
/// Used when brokerage is started as IDataQueueHandler
/// </summary>
public void SetJob(LiveNodePacket job)
{
    // Store job packet for later use
    _job = job;

    // Extract algorithm instance if available
    if (job?.AlgorithmId != null)
    {
        Log.Trace($"IGBrokerage.SetJob(): Algorithm ID: {job.AlgorithmId}");
    }

    // Initialize subscription manager if not already done
    if (_aggregator == null)
    {
        _aggregator = Composer.Instance.GetExportedValueByTypeName<IDataAggregator>(
            Config.Get("data-aggregator", "QuantConnect.Lean.Engine.DataFeeds.AggregationManager"));
    }

    // Set up any job-specific configuration
    if (job?.Channel != null)
    {
        Log.Trace($"IGBrokerage.SetJob(): Channel: {job.Channel}");
    }

    Log.Trace("IGBrokerage.SetJob(): Job configuration applied");
}

// Add field to store job
private LiveNodePacket _job;
```

---

## Implementation Timeline

### Week 1: Critical Gaps (P0)
- **Day 1:** Gap 1 (gh-actions.yml) + Gap 2 (config.json) - 1 hour
- **Day 2:** Gap 3 (ValidateSubscription) - 2 hours
- **Day 3-4:** Gap 4 (IGBrokerageTests) - 8 hours
- **Day 5:** Gap 5 (History Tests) + Testing - 4 hours

### Week 2: High Priority (P1)
- **Day 1-2:** Item 6 (BrokerageConcurrentMessageHandler) - 12 hours
- **Day 3-4:** Item 7 (Core Method Tests) - 12 hours
- **Day 5:** Item 8 (SetJob) + Final Testing - 4 hours

**Total Estimated Time:** 44 hours (~5.5 days)

---

## Testing Strategy

### Phase 1: Unit Tests
- Run all new unit tests individually
- Verify no regressions in existing tests
- Achieve >80% code coverage for new code

### Phase 2: Integration Tests
- Run full IGBrokerageTests suite
- Verify order lifecycle (place → fill → cancel)
- Test concurrent operations

### Phase 3: Manual Testing
- Connect to IG demo account
- Place real orders (small quantities)
- Verify WebSocket updates
- Test reconnection scenarios

### Phase 4: CI/CD Validation
- Verify GitHub Actions runs successfully
- Check all tests pass in CI environment
- Validate build artifacts

---

## Success Criteria

✅ All P0 items completed and tested
✅ All unit tests passing
✅ CI/CD pipeline green
✅ No [Ignore] or [NotImplementedException] in test code
✅ Code coverage >70% for modified files
✅ Manual testing successful on demo account
✅ Documentation updated

---

## Risk Mitigation

| Risk | Impact | Mitigation |
|------|--------|------------|
| IG API rate limits during testing | Test failures | Use longer delays, fewer test runs |
| Demo account connectivity issues | Cannot test | Have backup demo account |
| Breaking changes to existing code | Regression | Comprehensive unit tests before changes |
| Concurrent order race conditions | Production bugs | Extensive concurrent testing |

---

## Rollback Plan

If critical issues arise:
1. Revert to last stable commit
2. Isolate problematic changes to feature branch
3. Fix issues in isolation
4. Re-merge with additional testing

---

## Documentation Updates Required

1. **README.md** - Add testing instructions
2. **CONTRIBUTING.md** - Document test requirements
3. **config.example.json** - Credential setup guide
4. **API_DOCUMENTATION.md** - Document ValidateSubscription behavior

---

**Document End**
