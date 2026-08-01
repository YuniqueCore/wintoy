using System.Windows;
using FluentAssertions;
using FuturesTrader.Presentation.WindowHosting;

namespace FuturesTrader.Presentation.Tests.WindowHosting;

/// <summary>同组窗口以用户拖动窗口为锚点的纯坐标计算测试。</summary>
public class GroupSynchronizationCoordinatorTests
{
    [Fact]
    public void CalculateAlignedLayout_uses_anchor_final_top_and_packs_both_sides_without_overlap()
    {
        var windows = new[]
        {
            new GroupSynchronizationCoordinator.WindowBounds(10, 10, 300, 600),
            new GroupSynchronizationCoordinator.WindowBounds(500, 240, 320, 600),
            new GroupSynchronizationCoordinator.WindowBounds(900, 80, 280, 600)
        };

        var result = GroupSynchronizationCoordinator.CalculateAlignedLayout(
            windows, anchorIndex: 1, spacing: 4, new Rect(0, 0, 1600, 900));

        result.Select(item => item.Top).Should().OnlyContain(top => top == 240);
        result[0].Left.Should().Be(196);
        result[1].Left.Should().Be(500, "锚点能放入工作区时应保留用户最终横坐标");
        result[2].Left.Should().Be(824);
        AssertNoOverlap(result, windows, spacing: 4);
    }

    [Fact]
    public void CalculateAlignedLayout_shifts_the_whole_row_inside_work_area_without_changing_spacing()
    {
        var windows = new[]
        {
            new GroupSynchronizationCoordinator.WindowBounds(0, 100, 300, 700),
            new GroupSynchronizationCoordinator.WindowBounds(0, 100, 300, 700),
            new GroupSynchronizationCoordinator.WindowBounds(1500, 500, 300, 700)
        };

        var result = GroupSynchronizationCoordinator.CalculateAlignedLayout(
            windows, anchorIndex: 2, spacing: 4, new Rect(0, 0, 1600, 800));

        result[0].Left.Should().BeGreaterThanOrEqualTo(0);
        (result[^1].Left + windows[^1].Width).Should().BeLessThanOrEqualTo(1600);
        result.Select(item => item.Top).Should().OnlyContain(top => top == 100,
            "最高窗口为 700 时，Top 必须钳制到 800-700");
        AssertNoOverlap(result, windows, spacing: 4);
    }

    private static void AssertNoOverlap(
        IReadOnlyList<GroupSynchronizationCoordinator.WindowPlacement> placements,
        IReadOnlyList<GroupSynchronizationCoordinator.WindowBounds> windows,
        double spacing)
    {
        for (var index = 1; index < placements.Count; index++)
        {
            placements[index].Left.Should().BeGreaterThanOrEqualTo(
                placements[index - 1].Left + windows[index - 1].Width + spacing);
        }
    }
}
