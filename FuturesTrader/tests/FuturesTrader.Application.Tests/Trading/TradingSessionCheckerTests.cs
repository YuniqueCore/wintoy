using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;

namespace FuturesTrader.Application.Tests.Trading;

/// <summary>
/// TradingSessionChecker 单元测试：覆盖日盘/夜盘时段判断、收盘前 cutoff 拒单、非交易时段拒单。
/// 对齐 0527.exe sub_4DCBC0 的交易时段校验语义。
/// </summary>
public class TradingSessionCheckerTests
{
    private readonly ITradingSessionChecker _checker = new TradingSessionChecker();

    // ── IsInSession ──────────────────────────────────────────

    [Theory]
    [InlineData(9, 0, 0, true)]   // 日盘上午第一节开始
    [InlineData(9, 30, 0, true)]  // 日盘上午第一节中
    [InlineData(10, 14, 59, true)] // 日盘上午第一节末（含首不含尾，10:15:00 不在内）
    [InlineData(10, 15, 0, false)] // 休息段开始
    [InlineData(10, 29, 59, false)] // 休息段末
    [InlineData(10, 30, 0, true)]  // 日盘上午第二节开始
    [InlineData(11, 29, 59, true)] // 日盘上午第二节末
    [InlineData(11, 30, 0, false)] // 上午收盘
    [InlineData(13, 30, 0, true)]  // 日盘下午开始
    [InlineData(14, 59, 59, true)] // 日盘下午末
    [InlineData(15, 0, 0, false)]  // 日盘收盘
    [InlineData(15, 14, 59, false)] // 股指期货延时段（当前简化为不交易，待按品种扩展）
    [InlineData(15, 15, 0, false)] // 股指期货收盘
    [InlineData(21, 0, 0, true)]   // 夜盘开始
    [InlineData(23, 59, 59, true)] // 夜盘跨日前
    [InlineData(0, 0, 0, true)]    // 夜盘跨日后（00:00）
    [InlineData(2, 29, 59, true)]  // 夜盘末
    [InlineData(2, 30, 0, false)]  // 夜盘收盘
    [InlineData(8, 59, 59, false)] // 日盘开始前
    [InlineData(20, 59, 59, false)] // 夜盘开始前
    public void IsInSession_matches_standard_futures_sessions(int h, int m, int s, bool expected)
    {
        var now = DateTime.Today.Add(new TimeSpan(h, m, s));

        _checker.IsInSession(now).Should().Be(expected);
    }

    // ── CanPlaceOrder / CheckOrderAllowed ────────────────────

    [Fact]
    public void CheckOrderAllowed_allows_during_mid_session()
    {
        // 09:30 在上午第一节中段，远未到 cutoff
        var now = DateTime.Today.Add(new TimeSpan(9, 30, 0));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void CheckOrderAllowed_rejects_after_morning_session1_cutoff()
    {
        // 10:14:55 在上午第一节 cutoff 之后（cutoff = 10:14:50）
        var now = DateTime.Today.Add(new TimeSpan(10, 14, 55));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeFalse();
        reason.Should().Contain("cutoff");
    }

    [Fact]
    public void CheckOrderAllowed_rejects_after_morning_session2_cutoff()
    {
        // 11:29:55 在上午第二节 cutoff 之后（cutoff = 11:29:50）
        var now = DateTime.Today.Add(new TimeSpan(11, 29, 55));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeFalse();
        reason.Should().Contain("cutoff");
    }

    [Fact]
    public void CheckOrderAllowed_rejects_after_afternoon_cutoff()
    {
        // 14:59:55 在下午 cutoff 之后（cutoff = 14:59:50）
        var now = DateTime.Today.Add(new TimeSpan(14, 59, 55));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeFalse();
        reason.Should().Contain("cutoff");
    }

    [Fact]
    public void CheckOrderAllowed_rejects_after_night_session_cutoff()
    {
        // 02:29:55 在夜盘 cutoff 之后（cutoff = 02:29:50）
        var now = DateTime.Today.Add(new TimeSpan(2, 29, 55));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeFalse();
        reason.Should().Contain("夜盘");
    }

    [Fact]
    public void CheckOrderAllowed_rejects_non_trading_time()
    {
        // 12:00 午间休市
        var now = DateTime.Today.Add(new TimeSpan(12, 0, 0));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeFalse();
        reason.Should().Contain("非交易时段");
    }

    [Fact]
    public void CheckOrderAllowed_allows_just_before_cutoff()
    {
        // 10:14:49 在上午第一节 cutoff（10:14:50）之前 1 秒
        var now = DateTime.Today.Add(new TimeSpan(10, 14, 49));

        var (allowed, reason) = _checker.CheckOrderAllowed(now);

        allowed.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void CanPlaceOrder_consistent_with_CheckOrderAllowed()
    {
        var cases = new[]
        {
            new TimeSpan(9, 30, 0),    // mid-session → true
            new TimeSpan(10, 14, 55),  // cutoff → false
            new TimeSpan(12, 0, 0),    // non-session → false
            new TimeSpan(21, 30, 0),   // night session → true
        };

        foreach (var time in cases)
        {
            var now = DateTime.Today.Add(time);
            var (allowed, _) = _checker.CheckOrderAllowed(now);
            _checker.CanPlaceOrder(now).Should().Be(allowed,
                $"time {time} 的 CanPlaceOrder 应与 CheckOrderAllowed 一致");
        }
    }

    // ── TimeToNextSession ────────────────────────────────────

    [Fact]
    public void TimeToNextSession_returns_zero_when_in_session()
    {
        var now = DateTime.Today.Add(new TimeSpan(9, 30, 0));

        _checker.TimeToNextSession(now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void TimeToNextSession_returns_wait_time_before_morning_session()
    {
        // 08:00 → 距 09:00 还有 1 小时
        var now = DateTime.Today.Add(new TimeSpan(8, 0, 0));

        _checker.TimeToNextSession(now).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void TimeToNextSession_returns_wait_time_before_afternoon_session()
    {
        // 12:00 → 距 13:30 还有 1.5 小时
        var now = DateTime.Today.Add(new TimeSpan(12, 0, 0));

        _checker.TimeToNextSession(now).Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TimeToNextSession_returns_wait_time_before_night_session()
    {
        // 16:00 → 距 21:00 还有 5 小时
        var now = DateTime.Today.Add(new TimeSpan(16, 0, 0));

        _checker.TimeToNextSession(now).Should().Be(TimeSpan.FromHours(5));
    }
}
