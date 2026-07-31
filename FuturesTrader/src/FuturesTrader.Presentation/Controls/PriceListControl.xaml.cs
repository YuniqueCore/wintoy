using System.ComponentModel;
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
        if (item is not PriceLevel level) return UnquotedTemplate;
        if (level.DisplayZone == PriceDisplayZone.Unquoted) return UnquotedTemplate;
        if (level.IsLastPrice) return CenterTemplate;
        return level.DisplayZone == PriceDisplayZone.AskQuote ? AskTemplate : BidTemplate;
    }
}

/// <summary>
/// 五列价格梯控件。列 0 是按价撤单列，列 1/3 是交易列，列 2/4 只显示。
/// 中间无人报价行使用中性模板，但仍保留列 1/3 的点击命中面。
/// </summary>
public sealed partial class PriceListControl : UserControl
{
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

    public PriceListControl() => InitializeComponent();

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

    [Category("Behavior")]
    public int MouseWheelSpeed
    {
        get => (int)GetValue(MouseWheelSpeedProperty);
        set => SetValue(MouseWheelSpeedProperty, value);
    }

    private static void OnPriceLadderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PriceListControl control) control.ScrollToCenter();
    }

    private void ScrollToCenter()
    {
        if (PriceLadder is null || PriceItemsControl.Items.Count == 0) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PriceLadder is null) return;
            var centerIndex = PriceLadder.CenterIndex;
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

    /// <summary>第 1/3 列鼠标按下。左右键只选择数量事件，物理交易侧随事件一起上送。</summary>
    private void OnTradeCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || sender is not FrameworkElement { DataContext: PriceLevel level, Tag: PriceLadderTradeSide side }) return;
        var routedEvent = e.ChangedButton switch
        {
            MouseButton.Left => PriceSelectedEvent,
            MouseButton.Right => PriceRightClickedEvent,
            _ => null
        };
        if (routedEvent is null) return;

        RaiseEvent(new PriceSelectedEventArgs(routedEvent, this)
        {
            Price = level.Price,
            TradeSide = side
        });
        e.Handled = true;
    }

    /// <summary>第 0 列已有挂单时，左右键均可请求按价撤单。</summary>
    private void OnPendingOrderCellMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled
            || e.ChangedButton is not (MouseButton.Left or MouseButton.Right)
            || sender is not FrameworkElement { DataContext: PriceLevel level }
            || level.PendingOrderCount <= 0)
            return;

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
