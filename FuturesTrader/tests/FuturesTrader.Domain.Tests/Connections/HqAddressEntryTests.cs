using System.ComponentModel;
using FluentAssertions;
using FuturesTrader.Domain.Connections;

namespace FuturesTrader.Domain.Tests.Connections;

/// <summary>
/// <see cref="HqAddressEntry"/> 单元测试：锁定 INPC（INotifyPropertyChanged）契约。
/// <para>
/// 这是登录页 DataGrid 选中不丢失的设计基础：<see cref="HqAddressEntry"/> 必须是可变 + INPC 的 class，
/// 测速更新延迟时只改属性、不替换集合项实例，从而保持 DataGrid.SelectedItem 引用稳定。
/// 若有人误将其改回 sealed record，这些测试会立即失败，防止"输入密码后选中丢失"的竞态回归。
/// </para>
/// </summary>
public class HqAddressEntryTests
{
    [Fact]
    public void LatencyMs_set_to_new_value_raises_property_changed()
    {
        var entry = new HqAddressEntry { Name = "海通", Host = "1.2.3.4", Port = 38215 };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.LatencyMs = 42.5;

        raised.Should().ContainSingle().Which.Should().Be(nameof(HqAddressEntry.LatencyMs));
        entry.LatencyMs.Should().Be(42.5);
    }

    [Fact]
    public void ProbeSuccess_set_to_new_value_raises_property_changed()
    {
        var entry = new HqAddressEntry { Name = "海通", Host = "1.2.3.4", Port = 38215 };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.ProbeSuccess = false;

        raised.Should().ContainSingle().Which.Should().Be(nameof(HqAddressEntry.ProbeSuccess));
        entry.ProbeSuccess.Should().BeFalse();
    }

    [Fact]
    public void Setting_same_value_does_not_raise_property_changed()
    {
        var entry = new HqAddressEntry { LatencyMs = 30.0, ProbeSuccess = true };
        var raised = new List<string?>();
        ((INotifyPropertyChanged)entry).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        entry.LatencyMs = 30.0;
        entry.ProbeSuccess = true;

        raised.Should().BeEmpty();
    }

    [Fact]
    public void Updating_latency_keeps_instance_identity_stable()
    {
        // 模拟测速更新延迟：直接改属性，不替换实例。
        // DataGrid.SelectedItem 基于引用比较 —— 引用不变 ⇒ 选中不丢。
        var entry = new HqAddressEntry { Name = "海通", Host = "1.2.3.4", Port = 38215 };
        var originalRef = entry;

        entry.LatencyMs = 88.0;
        entry.ProbeSuccess = false;

        ReferenceEquals(originalRef, entry).Should().BeTrue();
        entry.LatencyMs.Should().Be(88.0);
        entry.ProbeSuccess.Should().BeFalse();
    }

    [Fact]
    public void Url_is_built_from_host_and_port()
    {
        var entry = new HqAddressEntry { Host = "180.168.212.75", Port = 38215 };
        entry.Url.Should().Be("tcp://180.168.212.75:38215");
    }

    [Fact]
    public void Defaults_latency_null_and_probe_success_true()
    {
        var entry = new HqAddressEntry();
        entry.LatencyMs.Should().BeNull();
        entry.ProbeSuccess.Should().BeTrue();
    }
}
