using FluentAssertions;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Presentation.Controls;

namespace FuturesTrader.Presentation.Tests.Controls;

/// <summary>价格梯白格过滤的纯布局契约测试。</summary>
public class PriceListLayoutTests
{
    [Fact]
    public void SelectVisibleRows_keeps_unquoted_rows_when_white_grid_is_on()
    {
        var ladder = BuildLadder();

        PriceListLayout.SelectVisibleRows(ladder, showWhiteGrid: true)
            .Should().HaveCount(ladder.Rows.Count);
    }

    [Fact]
    public void SelectVisibleRows_hides_all_unquoted_rows_when_white_grid_is_off()
    {
        var ladder = BuildLadder();
        var rows = PriceListLayout.SelectVisibleRows(ladder, showWhiteGrid: false);

        rows.Should().HaveCount(ladder.Rows.Count(row => row.DisplayZone != PriceDisplayZone.Unquoted));
        rows.Should().Contain(row => row.DisplayZone == PriceDisplayZone.AskQuote);
        rows.Should().Contain(row => row.DisplayZone == PriceDisplayZone.BidQuote);
        rows.Should().NotContain(row => row.DisplayZone == PriceDisplayZone.Unquoted);
    }

    [Fact]
    public void PriceListRows_updates_values_in_place_when_row_structure_is_unchanged()
    {
        var rows = new PriceListRows();
        var first = PriceListLayout.SelectVisibleRows(BuildLadder(), showWhiteGrid: true);
        rows.Apply(first).Should().BeTrue();
        var references = rows.Items.ToArray();
        var next = new DepthMarketData
        {
            InstrumentId = "ag2608",
            LastPrice = 102m,
            AskPrices = [103m],
            AskVolumes = [31],
            BidPrices = [101m],
            BidVolumes = [29],
        }.ToPriceLadder(priceTick: 1m, levels: 3);

        rows.Apply(next.Rows).Should().BeFalse("档数和各行显示区未变，不应重建按钮容器");

        rows.Items.Should().HaveSameCount(references);
        for (var index = 0; index < references.Length; index++)
            rows.Items[index].Should().BeSameAs(references[index]);
        rows.Items[3].Price.Should().Be(102m);
        rows.Items.Should().Contain(row => row.AskVolume == 31);
        rows.Items.Should().Contain(row => row.BidVolume == 29);
    }

    [Fact]
    public void PriceListRows_replaces_only_the_row_whose_template_kind_changes()
    {
        var rows = new PriceListRows();
        var initial = BuildLadder().Rows.ToArray();
        rows.Apply(initial);
        var references = rows.Items.ToArray();
        var changed = initial.ToArray();
        var centerIndex = Array.FindIndex(changed,
            row => row.DisplayZone == PriceDisplayZone.Unquoted);
        changed[centerIndex] = changed[centerIndex] with
        {
            DisplayZone = PriceDisplayZone.AskQuote,
            AskVolume = 9
        };

        rows.Apply(changed).Should().BeTrue();

        rows.Items[centerIndex].Should().NotBeSameAs(references[centerIndex]);
        rows.Items.Where((_, index) => index != centerIndex)
            .Should().Equal(references.Where((_, index) => index != centerIndex));
    }

    private static PriceLadder BuildLadder() => new DepthMarketData
    {
        InstrumentId = "ag2608",
        LastPrice = 100m,
        AskPrices = [101m],
        AskVolumes = [11],
        BidPrices = [99m],
        BidVolumes = [10]
    }.ToPriceLadder(priceTick: 1m, levels: 3);
}
