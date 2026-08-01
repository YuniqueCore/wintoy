using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.Controls;
using FuturesTrader.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 合约交易窗口（TYYWin 复刻）：独立 FluentWindow，每合约一个实例。
/// 标题 = "{合约} · 组 {N}"，尺寸来自 <see cref="InstrumentWindow"/>（Width/Height/Top/Left）。
/// KeyDown 转发 <see cref="IKeyboardOperationService"/> 集中派发；关闭时 Dispose ViewModel。
/// </summary>
public sealed partial class TradingWindow : FluentWindow
{
    private readonly IKeyboardOperationService? _keyboard;
    private readonly IGlobalOrderCancellationService? _globalCancellation;

    /// <summary>用于设计器（XAML 设计时预览）。</summary>
    public TradingWindow()
    {
        InitializeComponent();
    }

    /// <summary>运行时构造：注入键盘服务，DataContext 由外部设置为 TradingViewModel。</summary>
    public TradingWindow(IKeyboardOperationService keyboard, IGlobalOrderCancellationService? globalCancellation = null) : this()
    {
        _keyboard = keyboard;
        _globalCancellation = globalCancellation;
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (DataContext is TradingViewModel vm)
        {
            // PriceList 行数 = 2*Levels+1，Up/Down 导航在此范围
            var maxIndex = vm.PriceLadderLevels * 2;
            vm.RegisterKeyboardShortcuts(maxIndex);
        }
    }

    /// <summary>窗口级 KeyDown → 转发键盘服务集中派发（未命中则交还默认处理）。</summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        // Space 的范围由全局注册表决定，当前保守映射为所有可见交易窗。
        if (e.Key == Key.Space && _globalCancellation is not null)
        {
            _ = _globalCancellation.CancelAsync(GlobalOrderCancellationMode.SelectiveVisibleWindows);
            e.Handled = true;
            return;
        }

        if (_keyboard is not null && _keyboard.Handle(e))
        {
            e.Handled = true;
            return;
        }
        // 未命中已注册手势 → 默认处理
    }

    /// <summary>
    /// 价格梯左键点击 → 按 ValLeft 量挂单。方向由被点击的物理交易侧映射，
    /// 不由红蓝显示区或鼠标按键决定。
    /// </summary>
    private async void OnPriceLeftClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            await vm.OnPriceLeftClickedAsync(e.Price, e.TradeSide);
        }
    }

    /// <summary>
    /// 价格梯右键点击 → 按 ValRight 量挂单（新手禁用）。
    /// </summary>
    private async void OnPriceRightClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            await vm.OnPriceRightClickedAsync(e.Price, e.TradeSide);
        }
    }

    /// <summary>
    /// 价格梯挂单格点击 → 撤销该价位所有活动报单（用户点击 PendingOrderCount > 0 的格子）。
    /// </summary>
    private async void OnPendingOrderCancelClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            await vm.CancelOrdersAtPriceAsync(e.Price);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
