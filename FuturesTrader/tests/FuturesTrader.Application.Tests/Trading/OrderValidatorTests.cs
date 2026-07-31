using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Tests.Trading;

/// <summary>
/// OrderValidator 单元测试：覆盖下单前的纯校验链。
/// 用 Stub 时段校验器 + Stub 风控服务隔离外部依赖，聚焦校验顺序与拒绝原因。
/// </summary>
public class OrderValidatorTests
{
    private readonly StubSessionChecker _session = new();
    private readonly StubRiskService _risk = new();
    private readonly OrderValidator _validator;

    public OrderValidatorTests()
    {
        _validator = new OrderValidator(_session, _risk);
    }

    private static OrderRequest BuyOrder(decimal price = 300m, string instrument = "au2512") => new()
    {
        InstrumentId = instrument,
        Direction = Direction.Buy,
        OffsetFlag = OffsetFlag.Open,
        Price = price,
        Volume = 1,
        PriceTick = 0.2m
    };

    private static OrderValidationContext ValidContext() => new()
    {
        Now = new DateTime(2026, 7, 30, 9, 30, 0), // 交易时段内
        CurrentOrderCount = 0,
        CurrentPositionCount = 0
    };

    // ── 步骤 1：合约存在 ──────────────────────────────────────

    [Fact]
    public void Validate_rejects_empty_instrument()
    {
        var request = BuyOrder() with { InstrumentId = "" };

        var (allowed, reason) = _validator.Validate(request, ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Contain("合约代码");
    }

    [Fact]
    public void Validate_rejects_whitespace_instrument()
    {
        var request = BuyOrder() with { InstrumentId = "   " };

        var (allowed, reason) = _validator.Validate(request, ValidContext());

        allowed.Should().BeFalse();
    }

    // ── 步骤 2：交易时段 ──────────────────────────────────────

    [Fact]
    public void Validate_rejects_non_trading_session()
    {
        _session.SetAllowed(false, "非交易时段");

        var (allowed, reason) = _validator.Validate(BuyOrder(), ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Be("非交易时段");
    }

    [Fact]
    public void Validate_allows_during_trading_session()
    {
        _session.SetAllowed(true, null);

        var (allowed, _) = _validator.Validate(BuyOrder(), ValidContext());

        allowed.Should().BeTrue();
    }

    // ── 步骤 3：CBNearby 行情邻近保护 ─────────────────────────

    [Fact]
    public void Validate_rejects_when_relevant_market_update_is_too_recent()
    {
        var now = new DateTime(2026, 7, 30, 9, 30, 0);
        var ctx = ValidContext() with
        {
            NearbyEnabled = true,
            NearbyThrottleMs = 800,
            Now = now,
            LastRelevantMarketUpdate = now.AddMilliseconds(-200)
        };

        var (allowed, reason) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeFalse();
        reason.Should().Be("Chg Nearby!");
    }

    [Fact]
    public void Validate_allows_after_nearby_protection_window()
    {
        var now = new DateTime(2026, 7, 30, 9, 30, 0);
        var ctx = ValidContext() with
        {
            NearbyEnabled = true,
            NearbyThrottleMs = 800,
            Now = now,
            LastRelevantMarketUpdate = now.AddMilliseconds(-800)
        };

        var (allowed, _) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_allows_when_the_relevant_trade_side_has_no_market_update()
    {
        var ctx = ValidContext() with
        {
            NearbyEnabled = true,
            NearbyThrottleMs = 800,
            LastRelevantMarketUpdate = null
        };

        var (allowed, _) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_skips_nearby_when_disabled()
    {
        var now = new DateTime(2026, 7, 30, 9, 30, 0);
        var ctx = ValidContext() with
        {
            NearbyEnabled = false,
            NearbyThrottleMs = 800,
            Now = now,
            LastRelevantMarketUpdate = now
        };

        var (allowed, _) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeTrue();
    }

    // ── 步骤 5：对手价（CBMorderX）──────────────────────────

    [Fact]
    public void Validate_rejects_opponent_price_mode_without_valid_price()
    {
        var ctx = ValidContext() with
        {
            UseOpponentPrice = true,
            OpponentPrice = null
        };

        var (allowed, reason) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeFalse();
        reason.Should().Contain("对手价");
    }

    [Fact]
    public void Validate_rejects_opponent_price_mode_with_zero_price()
    {
        var ctx = ValidContext() with
        {
            UseOpponentPrice = true,
            OpponentPrice = 0m
        };

        var (allowed, _) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeFalse();
    }

    [Fact]
    public void Validate_allows_opponent_price_mode_with_valid_price()
    {
        var ctx = ValidContext() with
        {
            UseOpponentPrice = true,
            OpponentPrice = 300.2m
        };

        var (allowed, _) = _validator.Validate(BuyOrder(), ctx);

        allowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_rejects_zero_price_when_not_opponent_mode()
    {
        var request = BuyOrder(price: 0m);

        var (allowed, reason) = _validator.Validate(request, ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Contain("价格");
    }

    // ── 步骤 6：本地风控 ─────────────────────────────────────

    [Fact]
    public void Validate_rejects_when_local_risk_fails()
    {
        _risk.SetResult(false, "报单数超限");

        var (allowed, reason) = _validator.Validate(BuyOrder(), ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Be("报单数超限");
    }

    [Fact]
    public void Validate_passes_when_local_risk_passes()
    {
        _risk.SetResult(true, null);

        var (allowed, _) = _validator.Validate(BuyOrder(), ValidContext());

        allowed.Should().BeTrue();
    }

    // ── 步骤 7：价格 tick 校验 ───────────────────────────────

    [Fact]
    public void Validate_rejects_price_not_multiple_of_tick()
    {
        // tick=0.2，价格 300.15 不是 0.2 的整数倍
        var request = BuyOrder(price: 300.15m);
        request = request with { PriceTick = 0.2m };

        var (allowed, reason) = _validator.Validate(request, ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Contain("整数倍");
    }

    [Fact]
    public void Validate_allows_price_multiple_of_tick()
    {
        // tick=0.2，价格 300.0 是 0.2 的整数倍
        var request = BuyOrder(price: 300.0m);
        request = request with { PriceTick = 0.2m };

        var (allowed, _) = _validator.Validate(request, ValidContext());

        allowed.Should().BeTrue();
    }

    [Fact]
    public void Validate_skips_tick_check_when_tick_zero()
    {
        var request = BuyOrder(price: 300.15m);
        request = request with { PriceTick = 0m };

        var (allowed, _) = _validator.Validate(request, ValidContext());

        allowed.Should().BeTrue();
    }

    // ── 校验顺序 ─────────────────────────────────────────────

    [Fact]
    public void Validate_checks_session_before_risk()
    {
        // 非交易时段 + 风控拒绝 → 应返回"非交易时段"（先校验时段）
        _session.SetAllowed(false, "非交易时段");
        _risk.SetResult(false, "报单数超限");

        var (allowed, reason) = _validator.Validate(BuyOrder(), ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Be("非交易时段");
        _risk.CheckOrderCalled.Should().BeFalse("时段校验失败应短路，不调用风控");
    }

    [Fact]
    public void Validate_checks_risk_before_tick()
    {
        // 风控拒绝 + tick 不对 → 应返回风控原因（先校验风控）
        _risk.SetResult(false, "持仓超限");
        var request = BuyOrder(price: 300.15m) with { PriceTick = 0.2m };

        var (allowed, reason) = _validator.Validate(request, ValidContext());

        allowed.Should().BeFalse();
        reason.Should().Be("持仓超限");
    }

    // ── 全部通过 ─────────────────────────────────────────────

    [Fact]
    public void Validate_passes_all_checks_returns_true()
    {
        var request = BuyOrder(price: 300.2m) with { PriceTick = 0.2m };
        var ctx = ValidContext();

        var (allowed, reason) = _validator.Validate(request, ctx);

        allowed.Should().BeTrue();
        reason.Should().BeNull();
    }

    // ── Stubs ────────────────────────────────────────────────

    private sealed class StubSessionChecker : ITradingSessionChecker
    {
        private bool _allowed = true;
        private string? _reason;

        public void SetAllowed(bool allowed, string? reason)
        {
            _allowed = allowed;
            _reason = reason;
        }

        public bool IsInSession(DateTime now) => _allowed;
        public bool CanPlaceOrder(DateTime now) => _allowed;

        public (bool Allowed, string? Reason) CheckOrderAllowed(DateTime now)
            => (_allowed, _reason);

        public TimeSpan TimeToNextSession(DateTime now) =>
            _allowed ? TimeSpan.Zero : TimeSpan.FromHours(1);
    }

    private sealed class StubRiskService : ILocalRiskService
    {
        private bool _allowed = true;
        private string? _reason;

        public bool CheckOrderCalled { get; private set; }

        public void SetResult(bool allowed, string? reason)
        {
            _allowed = allowed;
            _reason = reason;
            CheckOrderCalled = false;
        }

        public (bool Allowed, string? Reason) CheckOrder(OrderRequest request, int currentOrderCount, int currentPositionCount)
        {
            CheckOrderCalled = true;
            return (_allowed, _reason);
        }

        public (bool Allowed, string? Reason) CheckCancel(string instrumentId, RiskCancelCounters cancelCounts)
            => (true, null);

        public void RecordCancel(string instrumentId) { }

        public void Reset() { }

        public RiskCancelCounters CurrentCounters => new();
    }
}
