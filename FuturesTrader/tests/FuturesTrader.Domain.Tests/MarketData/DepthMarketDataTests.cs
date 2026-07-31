using FluentAssertions;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Tests.MarketData;

/// <summary>
/// <see cref="DepthMarketData.ToPriceLadder"/> 映射测试：
/// 中心对称、IsLastPrice 唯一、5 档边界、价位就近匹配买卖盘量、PriceTick 步长。
/// 纯函数测试，无外部依赖。
/// </summary>
public class DepthMarketDataTests
{
    private static DepthMarketData BuildSnapshot(
        decimal last = 100m,
        decimal tick = 1m,
        decimal[]? bidPrices = null,
        int[]? bidVolumes = null,
        decimal[]? askPrices = null,
        int[]? askVolumes = null)
    {
        bidPrices ??= new[] { 99m, 98m, 97m, 96m, 95m };
        bidVolumes ??= new[] { 10, 20, 30, 40, 50 };
        askPrices ??= new[] { 101m, 102m, 103m, 104m, 105m };
        askVolumes ??= new[] { 11, 21, 31, 41, 51 };
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
    public void ToPriceLadder_produces_2n_plus_1_rows()
    {
        var data = BuildSnapshot();
        var ladder = data.ToPriceLadder(priceTick: 1m, levels: 5);
        ladder.Rows.Should().HaveCount(11, "5 档上下 + 1 中心 = 11 行");
        ladder.CenterIndex.Should().Be(5);
    }

    [Fact]
    public void ToPriceLadder_center_row_marks_is_last_price_unique()
    {
        var data = BuildSnapshot(last: 100m, tick: 1m);
        var ladder = data.ToPriceLadder(priceTick: 1m, levels: 5);
        var centerRows = ladder.Rows.Where(r => r.IsLastPrice).ToList();
        centerRows.Should().HaveCount(1, "只有中心行标记 IsLastPrice");
        ladder.Center!.IsLastPrice.Should().BeTrue();
        ladder.Center!.Price.Should().Be(100m);
    }

    [Fact]
    public void ToPriceLadder_uses_price_tick_as_step()
    {
        var data = BuildSnapshot(last: 100m, tick: 2m);
        var ladder = data.ToPriceLadder(priceTick: 2m, levels: 3);
        // 上方卖盘：100+2*3=106, 100+2*2=104, 100+2*1=102
        ladder.Rows[0].Price.Should().Be(106m);
        ladder.Rows[1].Price.Should().Be(104m);
        ladder.Rows[2].Price.Should().Be(102m);
        ladder.Center!.Price.Should().Be(100m);
        // 下方买盘：100-2*1=98, 100-2*2=96, 100-2*3=94
        ladder.Rows[4].Price.Should().Be(98m);
        ladder.Rows[5].Price.Should().Be(96m);
        ladder.Rows[6].Price.Should().Be(94m);
    }

    [Fact]
    public void ToPriceLadder_upper_rows_are_ask_lower_rows_are_bid()
    {
        var data = BuildSnapshot(last: 100m, tick: 1m);
        var ladder = data.ToPriceLadder(priceTick: 1m, levels: 5);
        // 上方第一行（最高价 105）对应 AskVolume，无 BidVolume
        var top = ladder.Rows[0];
        top.Price.Should().Be(105m);
        top.AskVolume.Should().Be(51, "AskPrice5=105 的量");
        top.BidVolume.Should().Be(0);
        // 下方第一行（最低价 95）对应 BidVolume，无 AskVolume
        var bottom = ladder.Rows[10];
        bottom.Price.Should().Be(95m);
        bottom.BidVolume.Should().Be(50, "BidPrice5=95 的量");
        bottom.AskVolume.Should().Be(0);
    }

    [Fact]
    public void ToPriceLadder_clamps_invalid_tick_and_levels()
    {
        var data = BuildSnapshot();
        // tick=0 应兜底为 1；levels=0 应兜底为 5
        var ladder = data.ToPriceLadder(priceTick: 0m, levels: 0);
        ladder.PriceTick.Should().Be(1m);
        ladder.Levels.Should().Be(5);
        ladder.Rows.Should().HaveCount(11);
    }

    [Fact]
    public void ToPriceLadder_handles_empty_books()
    {
        var data = BuildSnapshot(
            bidPrices: Array.Empty<decimal>(),
            bidVolumes: Array.Empty<int>(),
            askPrices: Array.Empty<decimal>(),
            askVolumes: Array.Empty<int>());
        var ladder = data.ToPriceLadder(priceTick: 1m, levels: 3);
        // 无买卖盘数据时，所有行量为 0，但中心行仍标记 IsLastPrice
        ladder.Rows.Should().HaveCount(7);
        ladder.Center!.IsLastPrice.Should().BeTrue();
        ladder.Rows.All(r => r.BidVolume == 0 && r.AskVolume == 0).Should().BeTrue();
        ladder.Rows.Should().OnlyContain(row => row.DisplayZone == PriceDisplayZone.Unquoted,
            "无人报价中间行不能被错误染成买方或卖方交易区");
    }

    [Fact]
    public void ToPriceLadder_marks_only_actual_quote_rows_with_a_colored_display_zone()
    {
        var data = BuildSnapshot(
            bidPrices: new[] { 99m },
            bidVolumes: new[] { 10 },
            askPrices: new[] { 101m },
            askVolumes: new[] { 11 });

        var ladder = data.ToPriceLadder(priceTick: 1m, levels: 3);

        ladder.Rows.Single(row => row.Price == 101m).DisplayZone.Should().Be(PriceDisplayZone.AskQuote);
        ladder.Rows.Single(row => row.Price == 99m).DisplayZone.Should().Be(PriceDisplayZone.BidQuote);
        ladder.Rows.Where(row => row.Price is 102m or 103m or 98m or 97m or 100m)
            .Should().OnlyContain(row => row.DisplayZone == PriceDisplayZone.Unquoted);
    }
}
