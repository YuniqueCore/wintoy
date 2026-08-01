using FluentAssertions;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Tests.MarketData;

/// <summary>
/// <see cref="PriceLadder"/> 值对象测试：真实最新价索引、三区计数和 PriceTick 不可变性。
/// 纯函数测试，无外部依赖。
/// </summary>
public class PriceLadderTests
{
    [Fact]
    public void CenterIndex_finds_the_row_marked_as_last_price()
    {
        var rows = new PriceLevel[] { new(), new(), new() { IsLastPrice = true } };
        var ladder = new PriceLadder(lastPrice: 100m, priceTick: 1m, rows);
        ladder.CenterIndex.Should().Be(2);
    }

    [Fact]
    public void Center_returns_row_at_center_index()
    {
        var center = new PriceLevel { Price = 100m, IsLastPrice = true };
        var rows = new PriceLevel[] { new() { Price = 101m }, center, new() { Price = 99m } };
        var ladder = new PriceLadder(lastPrice: 100m, priceTick: 1m, rows);
        ladder.Center.Should().Be(center);
        ladder.Center!.IsLastPrice.Should().BeTrue();
    }

    [Fact]
    public void Center_returns_null_when_no_row_is_marked_as_last_price()
    {
        var rows = new PriceLevel[] { new() };
        var ladder = new PriceLadder(lastPrice: 100m, priceTick: 1m, rows);
        ladder.CenterIndex.Should().Be(-1);
        ladder.Center.Should().BeNull();
    }

    [Fact]
    public void Properties_are_immutable_after_construction()
    {
        var rows = new PriceLevel[]
        {
            new() { DisplayZone = PriceDisplayZone.AskQuote },
            new() { DisplayZone = PriceDisplayZone.Unquoted },
            new() { DisplayZone = PriceDisplayZone.BidQuote }
        };
        var ladder = new PriceLadder(lastPrice: 100m, priceTick: 1m, rows);
        ladder.LastPrice.Should().Be(100m);
        ladder.PriceTick.Should().Be(1m);
        ladder.Rows.Should().HaveCount(3);
        ladder.AskQuoteRowCount.Should().Be(1);
        ladder.UnquotedRowCount.Should().Be(1);
        ladder.BidQuoteRowCount.Should().Be(1);
    }
}
