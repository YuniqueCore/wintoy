using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using FuturesTrader.Presentation.Services;

namespace FuturesTrader.Presentation.Tests.Services;

/// <summary>
/// <see cref="ThemeService"/> 的资源替换回归测试。
/// XAML 资源可能由 WPF 冻结；主题应用必须在该情况下替换资源，而不能修改冻结实例。
/// </summary>
public class ThemeServiceTests
{
    [Fact]
    public void ReplaceBrush_replaces_a_frozen_brush_with_a_writable_copy()
    {
        var resources = new ResourceDictionary();
        var original = new SolidColorBrush(Color.FromRgb(0x3D, 0x00, 0x00))
        {
            Opacity = 0.8,
        };
        original.Freeze();
        resources["PriceListAskRowBackgroundBrush"] = original;

        var expected = Color.FromRgb(0xFB, 0xE4, 0xE4);
        ThemeService.ReplaceBrush(resources, "PriceListAskRowBackgroundBrush", expected);

        var replacement = resources["PriceListAskRowBackgroundBrush"].Should().BeOfType<SolidColorBrush>().Which;
        replacement.Should().NotBeSameAs(original);
        replacement.IsFrozen.Should().BeFalse();
        replacement.Color.Should().Be(expected);
        replacement.Opacity.Should().Be(0.8);
        original.Color.Should().Be(Color.FromRgb(0x3D, 0x00, 0x00), "原冻结画刷不应被修改");
    }

    [Fact]
    public void ReplaceBrush_updates_a_writable_brush_in_place()
    {
        var resources = new ResourceDictionary();
        var original = new SolidColorBrush(Color.FromRgb(0x00, 0x1F, 0x3D));
        resources["PriceListBidRowBackgroundBrush"] = original;

        var expected = Color.FromRgb(0xE3, 0xEE, 0xFB);
        ThemeService.ReplaceBrush(resources, "PriceListBidRowBackgroundBrush", expected);

        resources["PriceListBidRowBackgroundBrush"].Should().BeSameAs(original);
        original.Color.Should().Be(expected);
    }
}
