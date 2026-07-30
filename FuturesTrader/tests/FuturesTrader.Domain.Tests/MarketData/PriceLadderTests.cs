using FluentAssertions;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Tests.MarketData;

/// <summary>
/// <see cref="PriceLadder"/> 值对象测试：中心索引、中心行、Levels/PriceTick 不可变性。
/// 纯函数测试，无外部依赖。
/// </summary>
public class PriceLadderTests
{
    [Fact]
    public void CenterIndex_returns_levels_value()
    {
        var rows = new PriceLevel[] { new(), new(), new() };
        var ladder = new PriceLadder(levels: 1, lastPrice: 100m, priceTick: 1m, rows);
        ladder.CenterIndex.Should().Be(1);
    }

    [Fact]
    public void Center_returns_row_at_center_index()
    {
        var center = new PriceLevel { Price = 100m, IsLastPrice = true };
        var rows = new PriceLevel[] { new() { Price = 101m }, center, new() { Price = 99m } };
        var ladder = new PriceLadder(levels: 1, lastPrice: 100m, priceTick: 1m, rows);
        ladder.Center.Should().Be(center);
        ladder.Center!.IsLastPrice.Should().BeTrue();
    }

    [Fact]
    public void Center_returns_null_when_rows_insufficient()
    {
        var rows = new PriceLevel[] { new() }; // 只有 1 行，levels=1 需要 3 行
        var ladder = new PriceLadder(levels: 1, lastPrice: 100m, priceTick: 1m, rows);
        ladder.Center.Should().BeNull("rows 不足以容纳中心行");
    }

    [Fact]
    public void Properties_are_immutable_after_construction()
    {
        var rows = new PriceLevel[] { new(), new(), new() };
        var ladder = new PriceLadder(levels: 1, lastPrice: 100m, priceTick: 1m, rows);
        ladder.Levels.Should().Be(1);
        ladder.LastPrice.Should().Be(100m);
        ladder.PriceTick.Should().Be(1m);
        ladder.Rows.Should().HaveCount(3);
    }
}
