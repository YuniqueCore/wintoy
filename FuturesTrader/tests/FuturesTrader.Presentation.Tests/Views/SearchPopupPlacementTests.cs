using System.Windows;
using FluentAssertions;
using FuturesTrader.Presentation.Views;

namespace FuturesTrader.Presentation.Tests.Views;

/// <summary>合约搜索候选弹层的工作区边界和展开方向测试。</summary>
public sealed class SearchPopupPlacementTests
{
    [Fact]
    public void Calculate_opens_above_and_aligns_right_when_target_is_near_bottom()
    {
        var popupSize = new Size(460, 320);
        var target = new Rect(860, 700, 180, 30);
        var workArea = new Rect(0, 0, 1080, 760);

        var offset = SearchPopupPlacement.Calculate(popupSize, target, workArea);
        var popupBounds = new Rect(target.Left + offset.X, target.Top + offset.Y, popupSize.Width, popupSize.Height);

        offset.X.Should().Be(target.Width - popupSize.Width);
        offset.Y.Should().Be(-popupSize.Height - 4);
        workArea.Contains(popupBounds).Should().BeTrue();
    }

    [Fact]
    public void Calculate_opens_below_and_clamps_left_when_target_is_near_top_left()
    {
        var popupSize = new Size(460, 320);
        var target = new Rect(10, 10, 180, 30);
        var workArea = new Rect(0, 0, 1080, 760);

        var offset = SearchPopupPlacement.Calculate(popupSize, target, workArea);
        var popupBounds = new Rect(target.Left + offset.X, target.Top + offset.Y, popupSize.Width, popupSize.Height);

        offset.Y.Should().Be(target.Height + 4);
        popupBounds.Left.Should().Be(workArea.Left);
        workArea.Contains(popupBounds).Should().BeTrue();
    }

    [Fact]
    public void Calculate_stays_inside_a_negative_coordinate_secondary_monitor()
    {
        var popupSize = new Size(460, 320);
        var target = new Rect(-240, 980, 180, 30);
        var workArea = new Rect(-1920, 0, 1920, 1040);

        var offset = SearchPopupPlacement.Calculate(popupSize, target, workArea);
        var popupBounds = new Rect(target.Left + offset.X, target.Top + offset.Y, popupSize.Width, popupSize.Height);

        workArea.Contains(popupBounds).Should().BeTrue();
        popupBounds.Right.Should().Be(target.Right);
    }
}
