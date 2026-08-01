using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Presentation.Controls;

/// <summary>按行情显示状态选择行模板。显示状态不参与价格梯下单方向决策。</summary>
public sealed class PriceLevelTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AskTemplate { get; set; }
    public DataTemplate? BidTemplate { get; set; }
    public DataTemplate? UnquotedTemplate { get; set; }
    public DataTemplate? CenterTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        var kind = item switch
        {
            PriceLevel level => PriceListLayout.GetTemplateKind(level),
            PriceListRow row => row.TemplateKind,
            _ => PriceRowTemplateKind.Unquoted,
        };
        return kind switch
        {
            PriceRowTemplateKind.Ask => AskTemplate,
            PriceRowTemplateKind.Bid => BidTemplate,
            PriceRowTemplateKind.Center => CenterTemplate,
            _ => UnquotedTemplate,
        };
    }
}

internal enum PriceRowTemplateKind
{
    Ask,
    Bid,
    Unquoted,
    Center,
}

/// <summary>价格梯白格过滤规则。无人报价只表达显示状态，不参与交易侧或方向决策。</summary>
internal static class PriceListLayout
{
    public static IReadOnlyList<PriceLevel> SelectVisibleRows(PriceLadder? ladder, bool showWhiteGrid)
    {
        if (ladder is null) return Array.Empty<PriceLevel>();
        if (showWhiteGrid) return ladder.Rows;
        return ladder.Rows
            .Where(row => row.DisplayZone != PriceDisplayZone.Unquoted)
            .ToArray();
    }

    internal static PriceRowTemplateKind GetTemplateKind(PriceLevel level)
    {
        if (level.DisplayZone == PriceDisplayZone.Unquoted) return PriceRowTemplateKind.Unquoted;
        if (level.IsLastPrice) return PriceRowTemplateKind.Center;
        return level.DisplayZone == PriceDisplayZone.AskQuote
            ? PriceRowTemplateKind.Ask
            : PriceRowTemplateKind.Bid;
    }
}

/// <summary>价格梯展示行：仅用于 WPF 原位更新，避免每个行情 tick 销毁按钮容器。</summary>
internal sealed class PriceListRow : INotifyPropertyChanged
{
    private decimal _price;
    private int _bidVolume;
    private int _askVolume;
    private int _pendingOrderCount;
    private bool _isLastPrice;
    private PriceDisplayZone _displayZone;

    internal PriceListRow(PriceLevel source) => Update(source);

    public decimal Price { get => _price; private set => SetField(ref _price, value); }

    public int BidVolume { get => _bidVolume; private set => SetField(ref _bidVolume, value); }

    public int AskVolume { get => _askVolume; private set => SetField(ref _askVolume, value); }

    public int PendingOrderCount { get => _pendingOrderCount; private set => SetField(ref _pendingOrderCount, value); }

    public bool IsLastPrice { get => _isLastPrice; private set => SetField(ref _isLastPrice, value); }

    public PriceDisplayZone DisplayZone { get => _displayZone; private set => SetField(ref _displayZone, value); }

    internal PriceRowTemplateKind TemplateKind => DisplayZone switch
    {
        PriceDisplayZone.Unquoted => PriceRowTemplateKind.Unquoted,
        _ when IsLastPrice => PriceRowTemplateKind.Center,
        PriceDisplayZone.AskQuote => PriceRowTemplateKind.Ask,
        _ => PriceRowTemplateKind.Bid,
    };

    internal void Update(PriceLevel source)
    {
        Price = source.Price;
        BidVolume = source.BidVolume;
        AskVolume = source.AskVolume;
        PendingOrderCount = source.PendingOrderCount;
        IsLastPrice = source.IsLastPrice;
        DisplayZone = source.DisplayZone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>协调不可变领域快照与稳定 WPF 行对象。</summary>
internal sealed class PriceListRows
{
    internal ObservableCollection<PriceListRow> Items { get; } = new();

    /// <returns>行结构是否变化；结构变化时控件才需要重新居中。</returns>
    internal bool Apply(IReadOnlyList<PriceLevel> nextRows)
    {
        if (Items.Count != nextRows.Count)
        {
            Items.Clear();
            foreach (var row in nextRows) Items.Add(new PriceListRow(row));
            return true;
        }

        var structureChanged = false;
        for (var index = 0; index < nextRows.Count; index++)
        {
            var next = nextRows[index];
            if (Items[index].TemplateKind != PriceListLayout.GetTemplateKind(next))
            {
                Items[index] = new PriceListRow(next);
                structureChanged = true;
            }
            else
            {
                Items[index].Update(next);
            }
        }
        return structureChanged;
    }
}

/// <summary>
/// 五列价格梯控件。列 0 是按价撤单列，列 1/3 是交易列，列 2/4 只显示。
/// 中间无人报价行使用中性模板，但仍保留列 1/3 的点击命中面。
/// </summary>
public sealed partial class PriceListControl : UserControl
{
    private readonly PriceListRows _rows = new();

    public static readonly DependencyProperty PriceLadderProperty =
        DependencyProperty.Register(
            nameof(PriceLadder),
            typeof(PriceLadder),
            typeof(PriceListControl),
            new PropertyMetadata(null, OnPriceLadderChanged));

    public static readonly DependencyProperty RowHeightProperty =
        DependencyProperty.Register(
            nameof(RowHeight),
            typeof(double),
            typeof(PriceListControl),
            new PropertyMetadata(18.0));

    public static readonly DependencyProperty ShowWhiteGridProperty =
        DependencyProperty.Register(
            nameof(ShowWhiteGrid),
            typeof(bool),
            typeof(PriceListControl),
            new PropertyMetadata(true, OnShowWhiteGridChanged));

    public static readonly DependencyProperty MouseWheelSpeedProperty =
        DependencyProperty.Register(
            nameof(MouseWheelSpeed),
            typeof(int),
            typeof(PriceListControl),
            new PropertyMetadata(3));

    /// <summary>左键交易请求，宿主按 ValLeft 取数量。</summary>
    public static readonly RoutedEvent PriceSelectedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(PriceSelected),
            RoutingStrategy.Bubble,
            typeof(EventHandler<PriceSelectedEventArgs>),
            typeof(PriceListControl));

    /// <summary>右键交易请求，宿主按 ValRight 取数量。</summary>
    public static readonly RoutedEvent PriceRightClickedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(PriceRightClicked),
            RoutingStrategy.Bubble,
            typeof(EventHandler<PriceSelectedEventArgs>),
            typeof(PriceListControl));

    /// <summary>第 0 列命中已有挂单时的按价撤单请求。</summary>
    public static readonly RoutedEvent PendingOrderCancelEvent =
        EventManager.RegisterRoutedEvent(
            nameof(PendingOrderCancel),
            RoutingStrategy.Bubble,
            typeof(EventHandler<PriceSelectedEventArgs>),
            typeof(PriceListControl));

    public PriceListControl()
    {
        InitializeComponent();
        PriceItemsControl.ItemsSource = _rows.Items;
    }

    public event EventHandler<PriceSelectedEventArgs> PriceSelected
    {
        add => AddHandler(PriceSelectedEvent, value);
        remove => RemoveHandler(PriceSelectedEvent, value);
    }

    public event EventHandler<PriceSelectedEventArgs> PriceRightClicked
    {
        add => AddHandler(PriceRightClickedEvent, value);
        remove => RemoveHandler(PriceRightClickedEvent, value);
    }

    public event EventHandler<PriceSelectedEventArgs> PendingOrderCancel
    {
        add => AddHandler(PendingOrderCancelEvent, value);
        remove => RemoveHandler(PendingOrderCancelEvent, value);
    }

    [Category("MarketData")]
    public PriceLadder? PriceLadder
    {
        get => (PriceLadder?)GetValue(PriceLadderProperty);
        set => SetValue(PriceLadderProperty, value);
    }

    [Category("Layout")]
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    [Category("Appearance")]
    public bool ShowWhiteGrid
    {
        get => (bool)GetValue(ShowWhiteGridProperty);
        set => SetValue(ShowWhiteGridProperty, value);
    }

    [Category("Behavior")]
    public int MouseWheelSpeed
    {
        get => (int)GetValue(MouseWheelSpeedProperty);
        set => SetValue(MouseWheelSpeedProperty, value);
    }

    private static void OnPriceLadderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PriceListControl control) control.RefreshVisibleRows();
    }

    private static void OnShowWhiteGridChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PriceListControl control) control.RefreshVisibleRows();
    }

    private void RefreshVisibleRows()
    {
        var structureChanged = _rows.Apply(PriceListLayout.SelectVisibleRows(PriceLadder, ShowWhiteGrid));
        if (structureChanged) ScrollToCenter();
    }

    private void ScrollToCenter()
    {
        if (PriceLadder is null || PriceItemsControl.Items.Count == 0) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            var centerIndex = -1;
            for (var index = 0; index < PriceItemsControl.Items.Count; index++)
            {
                if (PriceItemsControl.Items[index] is PriceListRow { IsLastPrice: true })
                {
                    centerIndex = index;
                    break;
                }
            }
            if (centerIndex < 0 || centerIndex >= PriceItemsControl.Items.Count) return;
            if (PriceItemsControl.ItemContainerGenerator.ContainerFromIndex(centerIndex) is FrameworkElement element)
                element.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var offset = e.Delta > 0 ? -Math.Max(1, MouseWheelSpeed) : Math.Max(1, MouseWheelSpeed);
        PriceScrollViewer.ScrollToVerticalOffset(PriceScrollViewer.VerticalOffset + offset * RowHeight);
    }

    /// <summary>第 1/3 列右键按下；左键统一走标准 Button.Click，兼容键盘和 UI Automation。</summary>
    private void OnTradeCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled
            || e.ChangedButton != MouseButton.Right
            || sender is not FrameworkElement { DataContext: PriceListRow level, Tag: PriceLadderTradeSide side })
            return;

        Focus();
        RaiseEvent(new PriceSelectedEventArgs(PriceRightClickedEvent, this)
        {
            Price = level.Price,
            TradeSide = side
        });
        e.Handled = true;
    }

    /// <summary>第 1/3 列标准左键/键盘激活入口。</summary>
    private void OnTradeCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PriceListRow level, Tag: PriceLadderTradeSide side }) return;
        Focus();
        RaiseEvent(new PriceSelectedEventArgs(PriceSelectedEvent, this)
        {
            Price = level.Price,
            TradeSide = side
        });
        e.Handled = true;
    }

    /// <summary>第 0 列已有挂单时，右键请求按价撤单。</summary>
    private void OnPendingOrderCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled
            || e.ChangedButton != MouseButton.Right
            || sender is not FrameworkElement { DataContext: PriceListRow level }
            || level.PendingOrderCount <= 0)
            return;

        RaiseEvent(new PriceSelectedEventArgs(PendingOrderCancelEvent, this) { Price = level.Price });
        e.Handled = true;
    }

    /// <summary>第 0 列标准左键/键盘激活入口。</summary>
    private void OnPendingOrderCellClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PriceListRow level } || level.PendingOrderCount <= 0) return;
        RaiseEvent(new PriceSelectedEventArgs(PendingOrderCancelEvent, this) { Price = level.Price });
        e.Handled = true;
    }
}

/// <summary>价格梯命中事件参数：价格和物理交易侧，绝不携带红蓝显示区来代替方向。</summary>
public sealed class PriceSelectedEventArgs : RoutedEventArgs
{
    public PriceSelectedEventArgs(RoutedEvent routedEvent, object source) : base(routedEvent, source) { }

    public decimal Price { get; init; }

    public PriceLadderTradeSide TradeSide { get; init; }
}
