using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FuturesTrader.Presentation.Controls;

/// <summary>
/// 互斥分段控件（单选 ToggleButton 群），用于替代 0527 浮动栏的 RadioButton 段（单/多/全部 · 仓/平 · A/B）。
/// <para>
/// 用法：
/// <code>
/// &lt;controls:SegmentedControl ItemsSource="{Binding DisplayModes}"
///                              SelectedValue="{Binding DisplayMode, Mode=TwoWay}"
///                              LabelSelector="Name" /&gt;
/// </code>
/// </para>
/// <para>
/// <see cref="ItemsSource"/> 接受任意 <see cref="IEnumerable"/>（推荐 enum 值或值对象）。
/// <see cref="SelectedValue"/> 用引用相等匹配（enum 自动装箱后正确比较）。
/// <see cref="LabelSelector"/> 是项上的属性名（反射读取作为按钮文本），例如 <c>"Name"</c> 或 <c>""</c>（直接 ToString）。
/// </para>
/// </summary>
public sealed partial class SegmentedControl : UserControl
{
    public SegmentedControl()
    {
        InitializeComponent();
        SegmentItems = new ObservableCollection<SegmentItem>();
    }

    /// <summary>内部渲染集合（每项一个 <see cref="SegmentItem"/>）。</summary>
    public ObservableCollection<SegmentItem> SegmentItems
    {
        get => (ObservableCollection<SegmentItem>)GetValue(SegmentItemsProperty);
        private set => SetValue(SegmentItemsPropertyKey, value);
    }

    private static readonly DependencyPropertyKey SegmentItemsPropertyKey =
        DependencyProperty.RegisterReadOnly(
            nameof(SegmentItems),
            typeof(ObservableCollection<SegmentItem>),
            typeof(SegmentedControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SegmentItemsProperty = SegmentItemsPropertyKey.DependencyProperty;

    /// <summary>外部数据源（enum 值、字符串、值对象均可）。</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SegmentedControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>当前选中值（与 ItemsSource 中某项引用相等即可）。</summary>
    public object? SelectedValue
    {
        get => GetValue(SelectedValueProperty);
        set => SetValue(SelectedValueProperty, value);
    }

    public static readonly DependencyProperty SelectedValueProperty =
        DependencyProperty.Register(
            nameof(SelectedValue),
            typeof(object),
            typeof(SegmentedControl),
            new PropertyMetadata(null, OnSelectedValueChanged));

    /// <summary>标签选择器：项上要展示为文本的属性名（空 = ToString()）。</summary>
    public string LabelSelector
    {
        get => (string)GetValue(LabelSelectorProperty);
        set => SetValue(LabelSelectorProperty, value);
    }

    public static readonly DependencyProperty LabelSelectorProperty =
        DependencyProperty.Register(
            nameof(LabelSelector),
            typeof(string),
            typeof(SegmentedControl),
            new PropertyMetadata(string.Empty));

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((SegmentedControl)d).RebuildSegments();
    }

    private static void OnSelectedValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (SegmentedControl)d;
        // 当 SegmentItems 还未填充（ItemsSource binding 还没触发）时，先记下初始值，
        // 等 RebuildSegments 完成时统一应用。否则首次默认选中可能因 SegmentItems 为空而丢失。
        if (ctrl.SegmentItems.Count == 0)
        {
            ctrl._pendingSelection = e.NewValue;
            return;
        }
        ctrl.SyncSelectionFromValue(e.NewValue);
    }

    /// <summary>在 SegmentItems 尚未填充时缓存的初始 SelectedValue，等 RebuildSegments 时使用。</summary>
    private object? _pendingSelection;

    private void RebuildSegments()
    {
        SegmentItems.Clear();
        if (ItemsSource is null) return;

        foreach (var item in ItemsSource)
        {
            var label = ResolveLabel(item);
            var tooltip = ResolveTooltip(item);
            SegmentItems.Add(new SegmentItem(item, label, tooltip, OnSegmentClicked));
        }

        // 优先用 _pendingSelection（在 SegmentItems 尚未填充时缓存的初始 SelectedValue），
        // 否则用当前 SelectedValue。
        SyncSelectionFromValue(_pendingSelection ?? SelectedValue);
        _pendingSelection = null;
    }

    private void OnSegmentClicked(object? value)
    {
        if (Equals(value, SelectedValue)) return;
        SelectedValue = value;
    }

    private void SyncSelectionFromValue(object? selected)
    {
        foreach (var seg in SegmentItems)
        {
            var matched = selected is not null && Equals(selected, seg.Value);
            seg.SetSelectedSilent(matched);
        }
    }

    private string ResolveLabel(object item)
    {
        if (string.IsNullOrEmpty(LabelSelector)) return item.ToString() ?? string.Empty;
        var prop = item.GetType().GetProperty(LabelSelector);
        return prop?.GetValue(item)?.ToString() ?? item.ToString() ?? string.Empty;
    }

    private string? ResolveTooltip(object item)
    {
        if (item is FrameworkElement fe) return fe.ToolTip?.ToString();
        return null;
    }
}

/// <summary>
/// 分段控件单项：<see cref="Value"/> 是原始数据，<see cref="Label"/> 是显示文本，
/// <see cref="IsSelected"/> 由 SegmentedControl 统一管理，<see cref="SelectCommand"/> 触发选中。
/// </summary>
public sealed class SegmentItem : ObservableObject
{
    private readonly Action<object?> _onSelect;
    private bool _isSelected;

    public SegmentItem(object value, string label, string? tooltip, Action<object?> onSelect)
    {
        Value = value;
        Label = label;
        Tooltip = tooltip;
        _onSelect = onSelect;
        SelectCommand = new RelayCommand(() => _onSelect(value));
    }

    /// <summary>原始值（与 <see cref="SegmentedControl.SelectedValue"/> 引用相等比较）。</summary>
    public object Value { get; }

    /// <summary>显示文本（按钮 Content）。</summary>
    public string Label { get; }

    /// <summary>鼠标悬停提示（可选）。</summary>
    public string? Tooltip { get; }

    /// <summary>是否当前选中（由 SegmentedControl 写，不允许用户直接改）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    /// <summary>点击触发选中（由 SegmentedControl 内部处理互斥）。</summary>
    public ICommand SelectCommand { get; }

    /// <summary>仅由 SegmentedControl 调用，避免循环触发。</summary>
    internal void SetSelectedSilent(bool value) => IsSelected = value;
}

/// <summary>占位（保留扩展点）：未来可加 <see cref="CollectionView"/> 过滤或 <see cref="ICollectionView"/> 分组支持。</summary>
internal static class SegmentedControlPlaceholder { }
