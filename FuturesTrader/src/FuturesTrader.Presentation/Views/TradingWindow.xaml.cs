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
/// 标题由合约元数据动态生成，尺寸来自 <see cref="InstrumentWindow"/>（Width/Height/Top/Left）。
/// KeyDown 先映射为配置动作，再在当前窗口或全局可见窗口范围执行；关闭时 Dispose ViewModel。
/// </summary>
public sealed partial class TradingWindow : FluentWindow
{
    private readonly IKeyboardOperationService? _keyboard;
    private readonly IGlobalOrderCancellationService? _globalCancellation;
    private readonly ITradingWindowInteractionService? _windowInteraction;

    /// <summary>用于设计器（XAML 设计时预览）。</summary>
    public TradingWindow()
    {
        InitializeComponent();
    }

    /// <summary>运行时构造：注入键盘服务，DataContext 由外部设置为 TradingViewModel。</summary>
    public TradingWindow(
        IKeyboardOperationService keyboard,
        IGlobalOrderCancellationService? globalCancellation = null,
        ITradingWindowInteractionService? windowInteraction = null) : this()
    {
        _keyboard = keyboard;
        _globalCancellation = globalCancellation;
        _windowInteraction = windowInteraction;
    }

    /// <summary>窗口级 KeyDown → 转发键盘服务集中派发（未命中则交还默认处理）。</summary>
    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (_keyboard is null) return;

        if (_keyboard.Matches(KeyboardShortcutAction.SelectiveCancelAll, e) && _globalCancellation is not null)
        {
            _ = _globalCancellation.CancelAsync(GlobalOrderCancellationMode.SelectiveVisibleWindows);
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.ForceCancelAll, e) && _globalCancellation is not null)
        {
            _ = _globalCancellation.CancelAsync(GlobalOrderCancellationMode.ForceAllWindows);
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.RecenterAsk, e) && _windowInteraction is not null)
        {
            _windowInteraction.RecenterVisiblePriceLadders(PriceLadderAnchor.Ask);
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.RecenterBid, e) && _windowInteraction is not null)
        {
            _windowInteraction.RecenterVisiblePriceLadders(PriceLadderAnchor.Bid);
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.ToggleOnlyOpen, e)
            && DataContext is TradingViewModel vm)
        {
            vm.CbOnlyOpen = !vm.CbOnlyOpen;
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.MoveSelectionUp, e))
        {
            ContractPriceList.MoveKeyboardSelection(-1);
            e.Handled = true;
            return;
        }
        if (_keyboard.Matches(KeyboardShortcutAction.MoveSelectionDown, e))
        {
            ContractPriceList.MoveKeyboardSelection(1);
            e.Handled = true;
        }
    }

    public void RecenterPriceLadder(PriceLadderAnchor anchor) => ContractPriceList.Recenter(anchor);

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
