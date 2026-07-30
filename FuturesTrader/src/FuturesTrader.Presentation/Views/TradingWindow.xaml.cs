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

    /// <summary>用于设计器（XAML 设计时预览）。</summary>
    public TradingWindow()
    {
        InitializeComponent();
    }

    /// <summary>运行时构造：注入键盘服务，DataContext 由外部设置为 TradingViewModel。</summary>
    public TradingWindow(IKeyboardOperationService keyboard) : this()
    {
        _keyboard = keyboard;
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
        // 空格键：全局撤销当前合约的所有活动报单（对齐 0527.exe 全局撤单习惯）。
        // 必须在键盘服务派发前拦截，否则 Up/Down 等已注册手势的 KeyGesture 不会冲突但空格也用于滚动，
        // 这里显式处理空格以避免滚动 + 不期望的派发。
        if (e.Key == Key.Space && DataContext is TradingViewModel vm)
        {
            if (vm.Order.ActiveOrderCount > 0)
            {
                _ = vm.CancelAllOrdersAsync();
                e.Handled = true;
                return;
            }
            // 没有活动报单时让 Space 继续走默认（避免误屏蔽其他用途）
        }

        if (_keyboard is not null && _keyboard.Handle(e))
        {
            e.Handled = true;
            return;
        }
        // 未命中已注册手势 → 默认处理
    }

    /// <summary>
    /// 价格梯左键点击 → 按 ValLeft 量挂单（红区挂空单 Sell，蓝区挂多单 Buy）。
    /// 对齐 0527.exe TPointWindow 点价挂单交互。
    /// </summary>
    private void OnPriceLeftClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            _ = vm.OnPriceLeftClickedAsync(e.Price, e.Zone);
        }
    }

    /// <summary>
    /// 价格梯右键点击 → 按 ValRight 量挂单（新手禁用）。
    /// </summary>
    private void OnPriceRightClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            _ = vm.OnPriceRightClickedAsync(e.Price, e.Zone);
        }
    }

    /// <summary>
    /// 价格梯挂单格点击 → 撤销该价位所有活动报单（用户点击 PendingOrderCount > 0 的格子）。
    /// </summary>
    private void OnPendingOrderCancelClicked(object sender, PriceSelectedEventArgs e)
    {
        if (DataContext is TradingViewModel vm)
        {
            _ = vm.CancelOrdersAtPriceAsync(e.Price);
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
