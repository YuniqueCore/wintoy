using FluentAssertions;
using FuturesTrader.Application;

namespace FuturesTrader.Application.Tests.Trading;

/// <summary>Mock 专用交易时段策略回归：全天允许且不返回伪造的拒绝原因。</summary>
public sealed class AlwaysAllowTradingSessionCheckerTests
{
    private readonly AlwaysAllowTradingSessionChecker _checker = new();

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 20, 0)]
    [InlineData(16, 45, 0)]
    [InlineData(23, 59, 59)]
    public void Allows_mock_orders_at_any_time(int hour, int minute, int second)
    {
        var now = DateTime.Today.Add(new TimeSpan(hour, minute, second));

        _checker.IsInSession(now).Should().BeTrue();
        _checker.CanPlaceOrder(now).Should().BeTrue();
        _checker.CheckOrderAllowed(now).Should().Be((true, null));
        _checker.TimeToNextSession(now).Should().Be(TimeSpan.Zero);
    }
}
