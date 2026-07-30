using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Presentation.Controls;

/// <summary>
/// <see cref="PriceLevel"/> 行模板选择器：中心行（<see cref="PriceLevel.IsLastPrice"/>）用高亮模板，
/// 上方卖盘（AskVolume&gt;0 且非中心）用红模板，下方买盘（BidVolume&gt;0 且非中心）用绿模板。
/// 由 XAML 的 ItemTemplateSelector 静态引用，运行时按行数据类型选模板。
/// </summary>
public sealed class PriceLevelTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AskTemplate { get; set; }
    public DataTemplate? BidTemplate { get; set; }
    public DataTemplate? CenterTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not PriceLevel level) return AskTemplate;
        if (level.IsLastPrice) return CenterTemplate;
        // 中心上方为卖盘（行索引 < 中心），下方为买盘；这里用 Volume 信号判断更鲁棒
        if (level.AskVolume > 0) return AskTemplate;
        return BidTemplate;
    }
}

/// <summary>
/// 价差居中价格列表 UserControl（TYYWin TStringGrid PriceList 复刻）。
/// <see cref="PriceLadder"/> DependencyProperty 绑定后渲染 2N+1 行（上方卖盘 → 中心最新价 → 下方买盘）。
/// 5 列按 PriceListRatios=10,25,30,25,10 比例（档位/买量/价格/卖量/价差）。
/// <see cref="MouseWheelSpeed"/> 控制滚轮加速（复刻旧软件 MouseWheelSpeed=3 习惯）。
/// 中心行加载后自动滚动到视口中央，使最新价始终居中可见。
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

    /// <summary>滚轮加速倍数（复刻旧软件 MouseWheelSpeed=3）。</summary>
    public static readonly DependencyProperty MouseWheelSpeedProperty =
        DependencyProperty.Register(
            nameof(MouseWheelSpeed),
            typeof(int),
            typeof(PriceListControl),
            new PropertyMetadata(3));

    /// <summary>
    /// 价位点击路由事件：点击价格梯某行时冒泡，携带该行价格。
    /// 对齐 0527.exe TPointWindow：点击价位 → 设置下单价格（LimitPrice）。
    /// </summary>
    public static readonly RoutedEvent PriceSelectedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(PriceSelected),
            RoutingStrategy.Bubble,
            typeof(EventHandler<PriceSelectedEventArgs>),
            typeof(PriceListControl));

    public PriceListControl()
    {
        InitializeComponent();
    }

    /// <summary>价位被点击时触发（携带该行价格，供宿主窗口设置下单 LimitPrice）。</summary>
    public event EventHandler<PriceSelectedEventArgs> PriceSelected
    {
        add => AddHandler(PriceSelectedEvent, value);
        remove => RemoveHandler(PriceSelectedEvent, value);
    }

    /// <summary>价格梯数据（行情刷新时整体替换）。</summary>
    [Category("MarketData")]
    public PriceLadder? PriceLadder
    {
        get => (PriceLadder?)GetValue(PriceLadderProperty);
        set => SetValue(PriceLadderProperty, value);
    }

    /// <summary>单行高度（像素，对应旧软件 RowHeight=12 等）。</summary>
    [Category("Layout")]
    public double RowHeight
    {
        get => (double)GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    /// <summary>滚轮加速倍数（每次滚动跨过 N 行，默认 3 复刻旧软件习惯）。</summary>
    [Category("Behavior")]
    public int MouseWheelSpeed
    {
        get => (int)GetValue(MouseWheelSpeedProperty);
        set => SetValue(MouseWheelSpeedProperty, value);
    }

    /// <summary>PriceLadder 变更后滚动到中心行（最新价始终居中可见）。</summary>
    private static void OnPriceLadderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PriceListControl control) return;
        control.ScrollToCenter();
    }

    /// <summary>把中心行（最新价）滚动到视口中央。</summary>
    private void ScrollToCenter()
    {
        if (PriceLadder is null || PriceItemsControl.Items.Count == 0) return;
        // 等 ItemsControl 渲染完再滚动（Dispatcher 后台优先级）
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (PriceLadder is null) return;
            var centerIndex = PriceLadder.CenterIndex;
            if (centerIndex < 0 || centerIndex >= PriceItemsControl.Items.Count) return;
            var container = PriceItemsControl.ItemContainerGenerator.ContainerFromIndex(centerIndex);
            if (container is not FrameworkElement element) return;
            element.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>滚轮加速：每次滚动跨过 MouseWheelSpeed 行（复刻旧软件习惯）。</summary>
    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;
        var speed = Math.Max(1, MouseWheelSpeed);
        // 向上滚（正 delta）→ 向上跨 speed 行；向下滚 → 向下跨 speed 行
        var offset = e.Delta > 0 ? -speed : speed;
        ScrollByRows(offset);
    }

    /// <summary>按行数滚动 ScrollViewer（每行高度 = RowHeight）。</summary>
    private void ScrollByRows(int rows)
    {
        var scroll = PriceScrollViewer;
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + rows * RowHeight);
    }

    /// <summary>
    /// 价格梯行点击：从命中元素向上遍历视觉树找到 <see cref="PriceLevel"/> DataContext，
    /// 触发 <see cref="PriceSelectedEvent"/> 冒泡。对齐 0527.exe TPointWindow 点价设价交互。
    /// </summary>
    private void OnPriceRowClick(object sender, MouseButtonEventArgs e)
    {
        var source = e.OriginalSource as DependencyObject;
        while (source is not null && !ReferenceEquals(source, this))
        {
            if (source is FrameworkElement fe && fe.DataContext is PriceLevel level)
            {
                RaiseEvent(new PriceSelectedEventArgs(PriceSelectedEvent, this)
                {
                    Price = level.Price
                });
                e.Handled = true;
                return;
            }
            source = VisualTreeHelper.GetParent(source);
        }
    }
}

/// <summary>价位点击事件参数：携带被点击行的价格。</summary>
public sealed class PriceSelectedEventArgs : RoutedEventArgs
{
    public PriceSelectedEventArgs(RoutedEvent routedEvent, object source) : base(routedEvent, source) { }

    /// <summary>被点击行的价格（已按 PriceTick 对齐）。</summary>
    public decimal Price { get; init; }
}
