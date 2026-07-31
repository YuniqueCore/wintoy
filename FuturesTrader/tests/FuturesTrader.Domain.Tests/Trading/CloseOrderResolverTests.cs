using FluentAssertions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Domain.Tests.Trading;

public class CloseOrderResolverTests
{
    [Fact]
    public void Only_open_keeps_open_even_when_opposite_positions_exist()
    {
        var result = CloseOrderResolver.Resolve(true, 5, new OppositePosition(3, 2, 0));

        result.Should().Be(new CloseOrderResolution(OffsetFlag.Open, 5));
    }

    [Fact]
    public void Fully_frozen_opposite_position_keeps_legacy_close_intent()
    {
        var result = CloseOrderResolver.Resolve(false, 5, new OppositePosition(3, 2, 5));

        result.Should().Be(new CloseOrderResolution(OffsetFlag.CloseToday, 3),
            "旧版点价用原始今昨仓位选择平仓，B 模式随后才能撤一笔满容量平仓单再替换");
    }

    [Fact]
    public void Available_today_position_uses_close_today_and_clamps_volume()
    {
        var result = CloseOrderResolver.Resolve(false, 5, new OppositePosition(3, 4, 0));

        result.Should().Be(new CloseOrderResolution(OffsetFlag.CloseToday, 3));
    }

    [Fact]
    public void Available_yesterday_position_uses_close_yesterday()
    {
        var result = CloseOrderResolver.Resolve(false, 5, new OppositePosition(0, 4, 0));

        result.Should().Be(new CloseOrderResolution(OffsetFlag.CloseYesterday, 4));
    }
}
