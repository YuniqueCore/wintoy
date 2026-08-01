using FluentAssertions;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Tests.MarketData;

/// <summary>
/// <see cref="DepthMarketData.ToPriceLadder"/> 边界驱动映射测试：
/// 卖一区、买一区分别按配置生成，二者之间的无人报价白格完全按价差自动计算。
/// </summary>
public class DepthMarketDataTests
{
    private static DepthMarketData BuildSnapshot(
        decimal last = 100m,
        decimal[]? bidPrices = null,
        int[]? bidVolumes = null,
        decimal[]? askPrices = null,
        int[]? askVolumes = null)
    {
        bidPrices ??= [99m, 98m, 97m, 96m, 95m];
        bidVolumes ??= [10, 20, 30, 40, 50];
        askPrices ??= [101m, 102m, 103m, 104m, 105m];
        askVolumes ??= [11, 21, 31, 41, 51];
        return new DepthMarketData
        {
            InstrumentId = "ag2608",
            LastPrice = last,
            BidPrices = bidPrices,
            BidVolumes = bidVolumes,
            AskPrices = askPrices,
            AskVolumes = askVolumes
        };
    }

    [Fact]
    public void ToPriceLadder_defaults_to_thirty_ask_auto_white_thirty_bid_rows()
    {
        var ladder = BuildSnapshot().ToPriceLadder(priceTick: 1m, askQuoteRowCount: 30, bidQuoteRowCount: 30);

        ladder.AskQuoteRowCount.Should().Be(30);
        ladder.UnquotedRowCount.Should().Be(1, "卖一 101 与买一 99 之间只有 100 一个白格");
        ladder.BidQuoteRowCount.Should().Be(30);
        ladder.Rows.Should().HaveCount(61);
        ladder.Rows[0].Price.Should().Be(130m);
        ladder.Rows[^1].Price.Should().Be(70m);
    }

    [Fact]
    public void ToPriceLadder_calculates_multiple_white_rows_from_spread()
    {
        var ladder = BuildSnapshot(
            bidPrices: [99m], bidVolumes: [10],
            askPrices: [104m], askVolumes: [11])
            .ToPriceLadder(priceTick: 1m, askQuoteRowCount: 5, bidQuoteRowCount: 6);

        ladder.AskQuoteRowCount.Should().Be(5);
        ladder.UnquotedRowCount.Should().Be(4);
        ladder.BidQuoteRowCount.Should().Be(6);
        ladder.Rows.Where(row => row.DisplayZone == PriceDisplayZone.Unquoted)
            .Select(row => row.Price)
            .Should().Equal(103m, 102m, 101m, 100m);
    }

    [Fact]
    public void ToPriceLadder_uses_price_tick_for_all_regions()
    {
        var ladder = BuildSnapshot(
            bidPrices: [98m], bidVolumes: [10],
            askPrices: [102m], askVolumes: [11])
            .ToPriceLadder(priceTick: 2m, askQuoteRowCount: 3, bidQuoteRowCount: 3);

        ladder.Rows.Select(row => row.Price).Should().Equal(106m, 104m, 102m, 100m, 98m, 96m, 94m);
        ladder.UnquotedRowCount.Should().Be(1);
    }

    [Fact]
    public void ToPriceLadder_marks_last_price_and_finds_its_real_index()
    {
        var ladder = BuildSnapshot().ToPriceLadder(priceTick: 1m, askQuoteRowCount: 7, bidQuoteRowCount: 9);

        ladder.Rows.Should().ContainSingle(row => row.IsLastPrice);
        ladder.CenterIndex.Should().Be(7, "中心索引来自真实 IsLastPrice 行，不再假设等于对称 levels");
        ladder.Center!.Price.Should().Be(100m);
    }

    [Fact]
    public void ToPriceLadder_maps_five_depth_volumes_without_fabricating_outer_levels()
    {
        var ladder = BuildSnapshot().ToPriceLadder(priceTick: 1m, askQuoteRowCount: 8, bidQuoteRowCount: 8);

        ladder.Rows.Single(row => row.Price == 105m).AskVolume.Should().Be(51);
        ladder.Rows.Single(row => row.Price == 95m).BidVolume.Should().Be(50);
        ladder.Rows.Single(row => row.Price == 108m).AskVolume.Should().Be(0,
            "延伸卖方可点击区域不能伪造 CTP 五档之外的量");
        ladder.Rows.Single(row => row.Price == 92m).BidVolume.Should().Be(0,
            "延伸买方可点击区域不能伪造 CTP 五档之外的量");
    }

    [Fact]
    public void ToPriceLadder_clamps_invalid_tick_and_row_counts()
    {
        var ladder = BuildSnapshot().ToPriceLadder(priceTick: 0m, askQuoteRowCount: 0, bidQuoteRowCount: 999);

        ladder.PriceTick.Should().Be(1m);
        ladder.AskQuoteRowCount.Should().Be(30, "非法非正数回退到产品默认 30");
        ladder.BidQuoteRowCount.Should().Be(100, "防止异常配置生成无界 UI 行");
    }

    [Fact]
    public void ToPriceLadder_handles_empty_books_without_inventing_quote_zones()
    {
        var ladder = BuildSnapshot(
            bidPrices: [], bidVolumes: [], askPrices: [], askVolumes: [])
            .ToPriceLadder(priceTick: 1m, askQuoteRowCount: 3, bidQuoteRowCount: 4);

        ladder.Rows.Should().HaveCount(8, "回退边界提供 3 + 最新价白格 + 4 个连续可点击价位");
        ladder.Center!.IsLastPrice.Should().BeTrue();
        ladder.Rows.Should().OnlyContain(row => row.DisplayZone == PriceDisplayZone.Unquoted);
        ladder.Rows.Should().OnlyContain(row => row.BidVolume == 0 && row.AskVolume == 0);
        ladder.AskQuoteRowCount.Should().Be(0);
        ladder.BidQuoteRowCount.Should().Be(0);
        ladder.UnquotedRowCount.Should().Be(8);
    }

    [Fact]
    public void ToPriceLadder_includes_pending_volume_in_each_generated_region()
    {
        var pending = new Dictionary<decimal, int> { [101m] = 3, [100m] = 2, [99m] = 4 };

        var ladder = BuildSnapshot().ToPriceLadder(1m, 2, 2, pending);

        ladder.Rows.Single(row => row.Price == 101m).PendingOrderCount.Should().Be(3);
        ladder.Rows.Single(row => row.Price == 100m).PendingOrderCount.Should().Be(2);
        ladder.Rows.Single(row => row.Price == 99m).PendingOrderCount.Should().Be(4);
    }
}
