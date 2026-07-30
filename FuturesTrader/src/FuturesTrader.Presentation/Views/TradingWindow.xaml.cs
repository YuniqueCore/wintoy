using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using FuturesTrader.Presentation.Abstractions;
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
        if (_keyboard is not null && _keyboard.Handle(e))
        {
            e.Handled = true;
            return;
        }
        // 未命中已注册手势 → 默认处理
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is IDisposable disposable)
            disposable.Dispose();
    }
}
