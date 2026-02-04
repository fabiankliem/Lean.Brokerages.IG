# README.md Compliance Implementation Plan

**Goal**: Achieve 100% compliance with QuantConnect brokerage development guide
**Current Status**: ~85% complete (excluding Lean/CLI integration)
**Timeline**: 8-12 hours of development work

---

## Phase 1: Critical Items (P0) - 3-4 hours

### 1.1 Update README.md References (30 min)
**Priority**: P0 - Critical
**Effort**: Low
**Files**: `README.md`

**Tasks**:
- [ ] Replace all "Template" → "IG Markets" throughout document
- [ ] Update title: "Lean.Brokerages.Template" → "Lean.Brokerages.IG"
- [ ] Update repository links if applicable
- [ ] Update badge URLs
- [ ] Add IG Markets specific documentation links

**Implementation**:
```bash
# In README.md
- Line 4: "# Lean.Brokerages.Template" → "# Lean.Brokerages.IG"
- Line 8: "Template Brokerage Plugin" → "IG Markets Brokerage Plugin"
- Add IG Markets API documentation link: https://labs.ig.com/
- Add supported features section specific to IG Markets
```

**Acceptance Criteria**:
- No references to "Template" remain in README.md
- All examples reference IG Markets
- Links point to IG documentation

---

### 1.2 Create brokerage.json Configuration (1 hour)
**Priority**: P0 - Critical
**Effort**: Medium
**Files**: New file at root: `brokerage.json`

**Tasks**:
- [ ] Create brokerage.json with IG Markets metadata
- [ ] Define supported security types
- [ ] Define supported order types
- [ ] Define supported resolutions
- [ ] Document data feed capabilities

**Implementation**:
```json
{
  "name": "IGBrokerage",
  "type": "QuantConnect.Brokerages.IG.IGBrokerage",
  "display-name": "IG Markets",
  "supported-security-types": [
    "Forex",
    "Index",
    "Cfd",
    "Crypto",
    "Equity"
  ],
  "supported-order-types": [
    "Market",
    "Limit",
    "StopMarket",
    "StopLimit"
  ],
  "supported-resolutions": [
    "Minute",
    "Hour",
    "Daily"
  ],
  "supported-markets": [
    "IG"
  ],
  "data-queue-handler": true,
  "history-provider": true,
  "live-trading": true,
  "paper-trading": true,
  "authentication": {
    "type": "api-key",
    "fields": [
      {
        "name": "ig-api-url",
        "type": "text",
        "label": "API URL",
        "default": "https://demo-api.ig.com/gateway/deal"
      },
      {
        "name": "ig-api-key",
        "type": "text",
        "label": "API Key",
        "help": "Get from https://labs.ig.com/"
      },
      {
        "name": "ig-identifier",
        "type": "text",
        "label": "Username"
      },
      {
        "name": "ig-password",
        "type": "password",
        "label": "Password"
      },
      {
        "name": "ig-account-id",
        "type": "text",
        "label": "Account ID (optional)"
      },
      {
        "name": "ig-environment",
        "type": "select",
        "label": "Environment",
        "options": ["demo", "live"],
        "default": "demo"
      }
    ]
  },
  "features": {
    "streaming": {
      "technology": "Lightstreamer",
      "supports-quotes": true,
      "supports-trades": true,
      "supports-order-updates": true
    },
    "trading": {
      "supports-updates": true,
      "supports-cancellation": true,
      "supports-stop-loss": true,
      "supports-take-profit": true,
      "supports-guaranteed-stops": false
    }
  },
  "rate-limits": {
    "trading": "40 requests/minute",
    "non-trading": "60 requests/minute"
  }
}
```

**Acceptance Criteria**:
- File exists at solution root
- Valid JSON format
- All fields accurately reflect IG Markets capabilities
- Matches current implementation

---

### 1.3 Implement OrderFee Calculation (1.5-2 hours)
**Priority**: P0 - Critical
**Effort**: Medium
**Files**:
- `QuantConnect.IGBrokerage/IGBrokerage.cs` (8 locations)
- New file: `QuantConnect.IGBrokerage/IGOrderFeeCalculator.cs`

**Tasks**:
- [ ] Research IG Markets fee structure
- [ ] Create IGOrderFeeCalculator class
- [ ] Implement fee calculation for different security types
- [ ] Update all OrderFee.Zero usages
- [ ] Add unit tests for fee calculation

**Current Locations Using OrderFee.Zero**:
```
Line 536: PlaceOrderInternal - invalid order
Line 555: PlaceOrderInternal - submitted
Line 604: PlaceOrderInternal - invalid with message
Line 665: PlaceOrderInternal - error response
Line 675: PlaceOrderInternal - success
Line 754: UpdateOrderInternal - update submitted
Line 812: CancelOrderInternal - canceled
Line 1214: ProcessTradeUpdate - order event
```

**Implementation**:

**File: `QuantConnect.IGBrokerage/IGOrderFeeCalculator.cs`** (New)
```csharp
namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// Calculates order fees for IG Markets
    /// </summary>
    public class IGOrderFeeCalculator
    {
        /// <summary>
        /// Calculate fee for an order
        /// </summary>
        /// <param name="order">The order</param>
        /// <param name="fillPrice">The fill price</param>
        /// <param name="fillQuantity">The fill quantity</param>
        /// <returns>Order fee</returns>
        public static OrderFee CalculateFee(Order order, decimal fillPrice, decimal fillQuantity)
        {
            if (order == null)
                return OrderFee.Zero;

            var orderValue = Math.Abs(fillPrice * fillQuantity);
            decimal feeAmount = 0m;
            string feeCurrency = "GBP";

            switch (order.SecurityType)
            {
                case SecurityType.Forex:
                    // IG Forex: Spread-based pricing (no commission)
                    // Fee already included in spread
                    return OrderFee.Zero;

                case SecurityType.Index:
                case SecurityType.Cfd:
                    // IG CFD/Index: 0.1% commission (min £10)
                    feeAmount = Math.Max(orderValue * 0.001m, 10m);
                    break;

                case SecurityType.Equity:
                    // IG Equity: 0.1% commission (min £10)
                    feeAmount = Math.Max(orderValue * 0.001m, 10m);
                    break;

                case SecurityType.Crypto:
                    // IG Crypto: Spread-based pricing
                    return OrderFee.Zero;

                default:
                    return OrderFee.Zero;
            }

            return new OrderFee(new CashAmount(feeAmount, feeCurrency));
        }

        /// <summary>
        /// Calculate fee for a filled order event
        /// </summary>
        public static OrderFee CalculateFee(OrderEvent orderEvent)
        {
            if (orderEvent?.Order == null)
                return OrderFee.Zero;

            return CalculateFee(
                orderEvent.Order,
                orderEvent.FillPrice,
                orderEvent.FillQuantity
            );
        }
    }
}
```

**File: `QuantConnect.IGBrokerage/IGBrokerage.cs` Updates**:
```csharp
// Line 555 - PlaceOrderInternal success
OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)  // ← CHANGE THIS
{
    Status = OrderStatus.Submitted
});

// TO:
OnOrderEvent(new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero) // Keep Zero for submit
{
    Status = OrderStatus.Submitted
});

// Line 1214 - ProcessTradeUpdate filled event
var orderEvent = new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)  // ← CHANGE THIS
{
    Status = status,
    Message = e.Reason
};

if (status == OrderStatus.Filled || status == OrderStatus.PartiallyFilled)
{
    orderEvent.FillPrice = e.FilledPrice ?? 0;
    orderEvent.FillQuantity = e.FilledSize ?? 0;
}

// TO:
var orderEvent = new OrderEvent(order, DateTime.UtcNow, OrderFee.Zero)
{
    Status = status,
    Message = e.Reason
};

if (status == OrderStatus.Filled || status == OrderStatus.PartiallyFilled)
{
    orderEvent.FillPrice = e.FilledPrice ?? 0;
    orderEvent.FillQuantity = e.FilledSize ?? 0;

    // Calculate actual fees for filled orders
    orderEvent.OrderFee = IGOrderFeeCalculator.CalculateFee(
        order,
        orderEvent.FillPrice,
        orderEvent.FillQuantity
    );
}
```

**Unit Tests**: `QuantConnect.IGBrokerage.Tests/IGOrderFeeCalculatorTests.cs` (New)
```csharp
[TestFixture]
public class IGOrderFeeCalculatorTests
{
    [Test]
    public void CalculatesFee_ForexOrder_ReturnsZero()
    {
        var order = new MarketOrder(
            Symbol.Create("EURUSD", SecurityType.Forex, Market.IG),
            1000,
            DateTime.UtcNow
        );

        var fee = IGOrderFeeCalculator.CalculateFee(order, 1.1000m, 1000);

        Assert.AreEqual(0m, fee.Value.Amount);
    }

    [Test]
    public void CalculatesFee_IndexOrder_ReturnsCorrectFee()
    {
        var order = new MarketOrder(
            Symbol.Create("SPX", SecurityType.Index, Market.IG),
            1,
            DateTime.UtcNow
        );

        var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, 1);

        // 4000 * 0.001 = 4, but min is 10
        Assert.AreEqual(10m, fee.Value.Amount);
        Assert.AreEqual("GBP", fee.Value.Currency);
    }

    [Test]
    public void CalculatesFee_LargeIndexOrder_ReturnsPercentageFee()
    {
        var order = new MarketOrder(
            Symbol.Create("SPX", SecurityType.Index, Market.IG),
            100,
            DateTime.UtcNow
        );

        var fee = IGOrderFeeCalculator.CalculateFee(order, 4000m, 100);

        // 400000 * 0.001 = 400
        Assert.AreEqual(400m, fee.Value.Amount);
    }
}
```

**Acceptance Criteria**:
- All OrderFee.Zero replaced with calculated fees where appropriate
- Fee calculation matches IG Markets pricing
- Unit tests pass
- Submitted orders still use OrderFee.Zero
- Filled orders calculate actual fees

---

## Phase 2: High Priority Items (P1) - 3-4 hours

### 2.1 Implement ReSubscription Process (2 hours)
**Priority**: P1 - High
**Effort**: High
**Files**:
- `QuantConnect.IGBrokerage/IGBrokerage.cs`
- `QuantConnect.IGBrokerage/Api/IGLightstreamerClient.cs`

**Tasks**:
- [ ] Implement market data reconnection handler
- [ ] Implement order updates reconnection handler
- [ ] Add CancellationToken support
- [ ] Store subscribed symbols for resubscription
- [ ] Add reconnection loop with exponential backoff

**Implementation**:

**Add to IGBrokerage.cs**:
```csharp
// Add fields
private CancellationTokenSource _cancellationTokenSource;
private Task _orderReconnectionTask;
private readonly ConcurrentDictionary<Symbol, bool> _subscribedSymbols;

// Constructor additions
_subscribedSymbols = new ConcurrentDictionary<Symbol, bool>();
_cancellationTokenSource = new CancellationTokenSource();

// Connect() additions
// Start reconnection monitoring
_orderReconnectionTask = Task.Run(() => MonitorOrderConnection(_cancellationTokenSource.Token));

// New method
private async Task MonitorOrderConnection(CancellationToken cancellationToken)
{
    var reconnectDelay = TimeSpan.FromSeconds(5);
    const int maxReconnectDelay = 60; // seconds

    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            // Wait for disconnection or cancellation
            await Task.Delay(reconnectDelay, cancellationToken);

            // Check if streaming client is disconnected
            if (_streamingClient != null && !_streamingClient.IsConnected && IsConnected)
            {
                Log.Trace("IGBrokerage.MonitorOrderConnection(): Streaming disconnected, attempting reconnection...");

                try
                {
                    // Reconnect streaming client
                    _streamingClient.Connect(
                        _lightstreamerEndpoint,
                        _cst,
                        _securityToken,
                        accountId
                    );

                    // Resubscribe to order updates
                    _streamingClient.SubscribeToOrders();

                    // Resubscribe to all market data
                    foreach (var symbol in _subscribedSymbols.Keys)
                    {
                        var epic = _symbolMapper.GetBrokerageSymbol(symbol);
                        if (!string.IsNullOrEmpty(epic))
                        {
                            _streamingClient.SubscribeToMarketData(epic);
                            Log.Trace($"IGBrokerage.MonitorOrderConnection(): Resubscribed to {symbol}");
                        }
                    }

                    Log.Trace("IGBrokerage.MonitorOrderConnection(): Reconnection successful");

                    // Reset delay on success
                    reconnectDelay = TimeSpan.FromSeconds(5);
                }
                catch (Exception ex)
                {
                    Log.Error($"IGBrokerage.MonitorOrderConnection(): Reconnection failed: {ex.Message}");

                    // Exponential backoff
                    reconnectDelay = TimeSpan.FromSeconds(
                        Math.Min(reconnectDelay.TotalSeconds * 2, maxReconnectDelay)
                    );
                }
            }
        }
        catch (TaskCanceledException)
        {
            // Expected on shutdown
            break;
        }
        catch (Exception ex)
        {
            Log.Error($"IGBrokerage.MonitorOrderConnection(): Error: {ex.Message}");
        }
    }

    Log.Trace("IGBrokerage.MonitorOrderConnection(): Monitoring stopped");
}

// Update Subscribe method to track symbols
public void Subscribe(Symbol symbol)
{
    // ... existing code ...

    // Track subscription
    _subscribedSymbols.TryAdd(symbol, true);

    // ... rest of existing code ...
}

// Update Unsubscribe method
public void Unsubscribe(Symbol symbol)
{
    // ... existing code ...

    // Remove from tracking
    _subscribedSymbols.TryRemove(symbol, out _);

    // ... rest of existing code ...
}

// Update Disconnect method
public override void Disconnect()
{
    // Cancel reconnection monitoring
    _cancellationTokenSource?.Cancel();

    try
    {
        _orderReconnectionTask?.Wait(TimeSpan.FromSeconds(5));
    }
    catch (Exception ex)
    {
        Log.Error($"IGBrokerage.Disconnect(): Error waiting for reconnection task: {ex.Message}");
    }

    // ... existing disconnect code ...
}

// Update Dispose
public override void Dispose()
{
    _cancellationTokenSource?.Dispose();
    _messageHandler?.Dispose();

    base.Dispose();
}
```

**Unit Test**: `QuantConnect.IGBrokerage.Tests/IGBrokerageReconnectionTests.cs` (New)
```csharp
[TestFixture, Explicit("Requires IG Markets credentials and manual disconnection")]
public class IGBrokerageReconnectionTests
{
    [Test]
    public void ReconnectsAfterNetworkDisconnection()
    {
        // This test requires manual intervention to disconnect network
        // or mock the Lightstreamer client disconnection

        // Setup
        var brokerage = CreateConnectedBrokerage();
        var symbol = Symbol.Create("EURUSD", SecurityType.Forex, Market.IG);

        // Subscribe
        var subscription = brokerage.Subscribe(
            new SubscriptionDataConfig(...),
            (s, e) => { }
        );

        Assert.IsTrue(brokerage.IsConnected);

        // Manual step: Disconnect network here
        Console.WriteLine("Please disconnect network for 10 seconds...");
        Thread.Sleep(15000);

        // Verify reconnection happened
        Assert.IsTrue(brokerage.IsConnected);
        // Verify data is flowing again
    }
}
```

**Acceptance Criteria**:
- Automatic reconnection on streaming client disconnect
- All subscriptions restored after reconnection
- Exponential backoff prevents rapid reconnection attempts
- CancellationToken properly stops monitoring on shutdown
- Order updates resume after reconnection

---

### 2.2 Implement Initialize() Pattern (1 hour)
**Priority**: P1 - High
**Effort**: Medium
**Files**: `QuantConnect.IGBrokerage/IGBrokerage.cs`

**Tasks**:
- [ ] Create Initialize() method consolidating setup
- [ ] Move Connect() logic into Initialize()
- [ ] Add validation in Initialize()
- [ ] Update tests to use Initialize()

**Implementation**:
```csharp
/// <summary>
/// Initializes the brokerage connection
/// </summary>
public void Initialize()
{
    if (_isConnected)
    {
        Log.Trace("IGBrokerage.Initialize(): Already initialized");
        return;
    }

    Log.Trace("IGBrokerage.Initialize(): Initializing IG Markets brokerage...");

    // Validate configuration
    if (string.IsNullOrEmpty(_apiKey) || string.IsNullOrEmpty(_identifier) || string.IsNullOrEmpty(_password))
    {
        throw new InvalidOperationException(
            "IGBrokerage.Initialize(): Missing required credentials. " +
            "Ensure ig-api-key, ig-identifier, and ig-password are configured."
        );
    }

    // Connect
    Connect();

    // Validate subscriptions if algorithm is available
    ValidateSubscriptions();

    Log.Trace("IGBrokerage.Initialize(): Initialization complete");
}
```

**Update Connect() to be called from Initialize()**:
```csharp
public override void Connect()
{
    // Keep existing implementation but make it callable from Initialize()
    // No changes needed to Connect() itself
}
```

**Acceptance Criteria**:
- Initialize() method exists and works
- Consolidates Connect() + ValidateSubscriptions()
- Validates configuration before connecting
- Tests updated to use Initialize()

---

### 2.3 Authentication Unit Tests (30-45 min)
**Priority**: P1 - High
**Effort**: Low-Medium
**Files**: New file `QuantConnect.IGBrokerage.Tests/IGRestApiClientTests.cs`

**Tasks**:
- [ ] Test successful authentication
- [ ] Test authentication with invalid credentials
- [ ] Test token refresh/expiration handling
- [ ] Test API request signing

**Implementation**:
```csharp
[TestFixture]
public class IGRestApiClientTests
{
    [Test, Explicit("Requires valid IG credentials")]
    public void Authenticate_ValidCredentials_Succeeds()
    {
        var apiUrl = Config.Get("ig-api-url");
        var apiKey = Config.Get("ig-api-key");
        var identifier = Config.Get("ig-identifier");
        var password = Config.Get("ig-password");

        if (string.IsNullOrEmpty(apiKey))
        {
            Assert.Ignore("Credentials not configured");
        }

        var client = new IGRestApiClient(apiUrl, apiKey);

        var (cst, securityToken, lightstreamerEndpoint) =
            client.Authenticate(identifier, password);

        Assert.IsNotNull(cst, "CST token should not be null");
        Assert.IsNotNull(securityToken, "Security token should not be null");
        Assert.IsNotNull(lightstreamerEndpoint, "Lightstreamer endpoint should not be null");
        Assert.IsFalse(string.IsNullOrEmpty(cst), "CST token should not be empty");
    }

    [Test]
    public void Authenticate_InvalidCredentials_ThrowsException()
    {
        var apiUrl = "https://demo-api.ig.com/gateway/deal";
        var apiKey = "invalid-key";
        var client = new IGRestApiClient(apiUrl, apiKey);

        Assert.Throws<Exception>(() =>
            client.Authenticate("invalid-user", "invalid-password")
        );
    }

    [Test, Explicit("Requires valid IG credentials")]
    public void MakesAuthenticatedRequest_AfterAuthentication_Succeeds()
    {
        // Setup
        var client = CreateAuthenticatedClient();

        // Test authenticated endpoint
        var accounts = client.GetAccounts();

        Assert.IsNotNull(accounts);
        Assert.IsNotEmpty(accounts);
    }

    private IGRestApiClient CreateAuthenticatedClient()
    {
        var apiUrl = Config.Get("ig-api-url");
        var apiKey = Config.Get("ig-api-key");
        var identifier = Config.Get("ig-identifier");
        var password = Config.Get("ig-password");

        var client = new IGRestApiClient(apiUrl, apiKey);
        client.Authenticate(identifier, password);

        return client;
    }
}
```

**Acceptance Criteria**:
- Authentication success test passes
- Invalid credentials test passes
- Authenticated request test validates tokens work
- Tests use [Explicit] attribute for credential requirements

---

### 2.4 Enhance BrokerageFactory Tests (30 min)
**Priority**: P1 - High
**Effort**: Low
**Files**: `QuantConnect.IGBrokerage.Tests/IGBrokerageFactoryTests.cs`

**Tasks**:
- [ ] Remove [Ignore] attribute, use [Explicit]
- [ ] Add tests for BrokerageData parsing
- [ ] Add tests for GetBrokerageModel()
- [ ] Add tests for Dispose()

**Implementation**:
```csharp
[TestFixture, Explicit("Requires IG configuration")]
public class IGBrokerageFactoryTests
{
    [Test]
    public void InitializesFactoryFromComposer()
    {
        using var factory = Composer.Instance.Single<IBrokerageFactory>(
            instance => instance.BrokerageType == typeof(IGBrokerage)
        );

        Assert.IsNotNull(factory);
        Assert.AreEqual(typeof(IGBrokerage), factory.BrokerageType);
    }

    [Test]
    public void CreatesBrokerage_WithValidConfiguration()
    {
        var factory = new IGBrokerageFactory();
        var brokerageData = new Dictionary<string, string>
        {
            { "ig-api-url", "https://demo-api.ig.com/gateway/deal" },
            { "ig-api-key", Config.Get("ig-api-key", "test-key") },
            { "ig-identifier", Config.Get("ig-identifier", "test-user") },
            { "ig-password", Config.Get("ig-password", "test-pass") },
            { "ig-account-id", "" },
            { "ig-environment", "demo" }
        };

        var job = new LiveNodePacket { BrokerageData = brokerageData };

        var brokerage = factory.CreateBrokerage(job, null);

        Assert.IsNotNull(brokerage);
        Assert.IsInstanceOf<IGBrokerage>(brokerage);

        brokerage.Dispose();
    }

    [Test]
    public void GetsBrokerageModel_ReturnsCorrectModel()
    {
        var factory = new IGBrokerageFactory();

        var model = factory.GetBrokerageModel(null);

        Assert.IsNotNull(model);
        // Verify model properties
    }

    [Test]
    public void ParsesBrokerageData_ExtractsAllFields()
    {
        var factory = new IGBrokerageFactory();
        var brokerageData = new Dictionary<string, string>
        {
            { "ig-api-url", "https://api.ig.com/gateway/deal" },
            { "ig-api-key", "my-key" },
            { "ig-identifier", "my-user" },
            { "ig-password", "my-pass" },
            { "ig-account-id", "ABC123" },
            { "ig-environment", "live" }
        };

        // This tests internal parsing - may need to expose for testing
        // or test through CreateBrokerage
        var job = new LiveNodePacket { BrokerageData = brokerageData };
        var brokerage = factory.CreateBrokerage(job, null) as IGBrokerage;

        Assert.IsNotNull(brokerage);
        brokerage.Dispose();
    }
}
```

**Acceptance Criteria**:
- [Ignore] changed to [Explicit]
- All tests pass
- Factory creation tested
- BrokerageData parsing validated

---

## Phase 3: Optional/Future Items (P2-P3) - 2-3 hours

### 3.1 IDataQueueUniverseProvider for Options (2-3 hours)
**Priority**: P2 - Medium (Optional)
**Effort**: High
**Decision Required**: Does IG integration need option chain support?

**If YES - Implement**:
```csharp
// New file: IGBrokerage.DataQueueUniverseProvider.cs
public partial class IGBrokerage : IDataQueueUniverseProvider
{
    public IEnumerable<Symbol> LookupSymbols(Symbol symbol, bool includeExpired, string securityCurrency = null)
    {
        if (symbol.SecurityType != SecurityType.Option &&
            symbol.SecurityType != SecurityType.IndexOption)
        {
            return Enumerable.Empty<Symbol>();
        }

        // Call IG API to get option chain
        var underlying = symbol.Underlying;
        var epic = _symbolMapper.GetBrokerageSymbol(underlying);

        // Get option contracts from IG
        var contracts = _restClient.GetOptionContracts(epic);

        // Convert to Lean symbols
        return contracts.Select(c => ConvertToLeanOptionSymbol(c));
    }

    public bool CanPerformSelection() => true;
}
```

**If NO - Document Limitation**:
Add to brokerage.json:
```json
"limitations": {
  "options": "Option chains not supported in current version"
}
```

---

## Summary & Timeline

### Total Effort Estimate
- **P0 Items**: 3-4 hours
- **P1 Items**: 3-4 hours
- **P2 Items**: 2-3 hours (if needed)
- **Total**: 8-11 hours

### Recommended Implementation Order
1. ✅ README.md updates (30 min) - Quick win
2. ✅ brokerage.json creation (1 hour) - Documentation
3. ✅ OrderFee calculation (2 hours) - Core functionality
4. ✅ Authentication tests (45 min) - Quality
5. ✅ BrokerageFactory tests (30 min) - Quality
6. ✅ Initialize() pattern (1 hour) - Best practice
7. ✅ ReSubscription process (2 hours) - Robustness
8. ⚠️ IDataQueueUniverseProvider (3 hours) - Optional

### Post-Implementation Checklist
- [ ] All unit tests pass
- [ ] Integration tests pass
- [ ] README.md accurate
- [ ] brokerage.json valid
- [ ] No OrderFee.Zero for filled orders
- [ ] Reconnection tested manually
- [ ] Documentation updated
- [ ] Commit with detailed message
- [ ] Update compliance status to 95-100%

### Success Criteria
- ✅ 100% P0 items complete
- ✅ 100% P1 items complete
- ✅ README compliance: 95-100%
- ✅ All tests passing
- ✅ Production-ready code quality

---

## Next Steps

1. **Review this plan** - Confirm priorities and scope
2. **Start with P0** - Quick wins for immediate compliance boost
3. **Progress to P1** - Enhanced quality and robustness
4. **Decide on P2** - Evaluate need for option chain support
5. **Final validation** - Comprehensive testing
6. **Documentation** - Update all docs to reflect changes

**Ready to proceed?** I can start implementing these items systematically, beginning with the P0 critical items.
