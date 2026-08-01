using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using FluentAssertions;
using FuturesTrader.Presentation.Controls;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Tests.Controls;

/// <summary>
/// <see cref="SegmentItem"/> 单元测试：覆盖选中状态切换、SelectCommand 回调、值标识稳定性。
/// <see cref="SegmentedControl"/> 是 WPF UserControl（WPF Control 构造强制要求 STA），其渲染层
/// 在 PresentationTests 项目中无法在 MTA 上实例化；本测试只覆盖数据层（SegmentItem）和算法层（匹配），
/// 渲染层由 UiAutomationTests 在真实 STA 线程上覆盖。
/// </summary>
public class SegmentedControlTests
{
    [Fact]
    public void SegmentItem_Holds_Value_Label_Tooltip_As_Provided()
    {
        var item = new SegmentItem(42, "answer", "the answer", _ => { });

        item.Value.Should().Be(42);
        item.Label.Should().Be("answer");
        item.Tooltip.Should().Be("the answer");
        item.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void SegmentItem_SelectCommand_Invokes_Callback_With_Value()
    {
        var option = 42;
        object? captured = null;
        var item = new SegmentItem(option, "answer", null, v => captured = v);

        item.SelectCommand.Should().NotBeNull();
        if (item.SelectCommand.CanExecute(null))
        {
            item.SelectCommand.Execute(null);
        }

        captured.Should().Be(option);
    }

    [Fact]
    public void SegmentItem_SelectCommand_For_Reference_Type_Passes_Same_Instance()
    {
        var option = new TestOption(1, "x");
        object? captured = null;
        var item = new SegmentItem(option, "x", null, v => captured = v);

        item.SelectCommand.Execute(null);

        captured.Should().BeSameAs(option);
    }

    [Fact]
    public void SegmentItem_SetSelectedSilent_Updates_IsSelected_Without_Notification_Loop()
    {
        var item = new SegmentItem(1, "x", null, _ => { });
        var propertyChangedCount = 0;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SegmentItem.IsSelected)) propertyChangedCount++;
        };

        item.SetSelectedSilent(true);
        item.IsSelected.Should().BeTrue();
        propertyChangedCount.Should().Be(1);

        // Same value: SetProperty uses EqualityComparer.Default → no duplicate event
        item.SetSelectedSilent(true);
        propertyChangedCount.Should().Be(1, "重复设置相同值不应再次通知");

        item.SetSelectedSilent(false);
        item.IsSelected.Should().BeFalse();
        propertyChangedCount.Should().Be(2);
    }

    [Fact]
    public void SegmentItem_Equality_Cases_For_Reference_And_Value_Types()
    {
        // enum values are value-type boxed but compare by underlying value
        var a = FloatingOrderMode.Open;
        var b = FloatingOrderMode.Open;
        // ReferenceEquals on value types boxes them separately → never equal
        // (but SegmentedControl uses Equals, which compares underlying value)
        a.Equals(b).Should().BeTrue("Equals on enums uses underlying value");

        // For SegmentedControl.SelectedValue, Equals on boxed enums is what matters
        var items = new[]
        {
            new SegmentItem(FloatingOrderMode.Open, "仓", null, _ => {}),
            new SegmentItem(FloatingOrderMode.Close, "平", null, _ => {}),
        };
        items[0].Value.Equals(FloatingOrderMode.Open).Should().BeTrue();
        items[1].Value.Equals(FloatingOrderMode.Open).Should().BeFalse();
    }

    [Fact]
    public void OptionItem_Record_Equality_Works_For_Enum_Value()
    {
        // OptionItem is a record → equality based on all properties (Value + Label)
        var a = new OptionItem(FloatingOrderMode.Open, "仓");
        var b = new OptionItem(FloatingOrderMode.Open, "仓");
        var c = new OptionItem(FloatingOrderMode.Open, "Open");

        a.Should().Be(b);
        a.Should().NotBe(c, "Label 不同的 OptionItem 不应相等");
    }

    [Fact]
    public void ValueSelector_resolves_enum_instead_of_writing_OptionItem_to_enum_binding()
    {
        var option = new OptionItem(FloatingOrderMode.Open, "仓", "开仓模式");

        SegmentedControl.ResolveMember(option, "Value").Should().Be(FloatingOrderMode.Open);
        SegmentedControl.ResolveMember(option, "Label").Should().Be("仓");
        SegmentedControl.ResolveMember(option, "Description").Should().Be("开仓模式");
    }

    /// <summary>测试用值对象。</summary>
    private sealed record TestOption(int Id, string Name);
}
