using System.Windows;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// <see cref="IWindowHost"/> 的真实实现：用 <see cref="TradingWindow"/>（TYYWin 复刻）替换 <see cref="StubWindowHost"/>。
/// 注入 <see cref="IServiceProvider"/>（用 <c>ActivatorUtilities</c> 创建每合约 VM）+ <see cref="IKeyboardOperationService"/>。
/// <see cref="Open(InstrumentWindow)"/>：已开则 <c>Activate</c>；否则构造 <see cref="TradingViewModel"/>（合约码作为构造参数）
/// → <c>new TradingWindow(keyboard){Title, Width, Height, Top, Left, DataContext=vm}</c> → 记入字典 → <c>Show()</c>。
/// 全部 <see cref="Dispatcher.Invoke"/> 包裹：MCP HTTP 线程触发时 marshalled 到 UI 线程，避免跨线程异常。
/// TradingWindow 关闭时由其 OnClosing Dispose ViewModel（退订行情），Closed 事件移除字典项。
/// </summary>
public sealed class WindowManager : IWindowHost
{
    private readonly IServiceProvider _services;
    private readonly IKeyboardOperationService _keyboard;
    private readonly ILogger<WindowManager> _logger;
    private readonly Dictionary<string, Window> _open = new(StringComparer.Ordinal);

    public WindowManager(
        IServiceProvider services,
        IKeyboardOperationService keyboard,
        ILogger<WindowManager> logger)
    {
        _services = services;
        _keyboard = keyboard;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsOpen(string instrumentCode)
    {
        ArgumentNullException.ThrowIfNull(instrumentCode);
        lock (_open) return _open.ContainsKey(instrumentCode);
    }

    /// <inheritdoc />
    public void Open(InstrumentWindow window) => OnUi(() =>
    {
        ArgumentNullException.ThrowIfNull(window);
        if (string.IsNullOrWhiteSpace(window.InstrumentCode)) return;

        lock (_open)
        {
            if (_open.TryGetValue(window.InstrumentCode, out var existing))
            {
                existing.Activate();
                return;
            }
        }

        // 用 ActivatorUtilities 创建 TradingViewModel：合约码作为首参，其余从 DI 解析
        var vm = (TradingViewModel)ActivatorUtilities.CreateInstance(
            _services, typeof(TradingViewModel), window.InstrumentCode);

        var tradingWindow = new TradingWindow(_keyboard)
        {
            Title = $"{window.InstrumentCode} · 组 {window.GroupId}",
            Width = Math.Max(window.Width, 320),
            Height = Math.Max(window.Height, 480),
            Left = window.Left,
            Top = window.Top,
            DataContext = vm
        };

        tradingWindow.Closed += (_, _) =>
        {
            lock (_open) _open.Remove(window.InstrumentCode);
            _logger.LogInformation("合约窗口已关闭: {Instrument}", window.InstrumentCode);
        };

        lock (_open) _open[window.InstrumentCode] = tradingWindow;
        tradingWindow.Show();
        _logger.LogInformation("合约窗口已打开: {Instrument} (组 {Group})", window.InstrumentCode, window.GroupId);
    });

    /// <inheritdoc />
    public void Focus(string instrumentCode) => OnUi(() =>
    {
        lock (_open)
        {
            if (_open.TryGetValue(instrumentCode, out var w))
                w.Activate();
        }
    });

    /// <inheritdoc />
    public void Close(string instrumentCode) => OnUi(() =>
    {
        lock (_open)
        {
            if (_open.TryGetValue(instrumentCode, out var w))
                w.Close();
        }
    });

    /// <summary>在 UI 线程执行；无 WPF 应用上下文时（如单元测试）直接内联执行。
    /// 用完全限定 System.Windows.Application 避免与 FuturesTrader.Application 命名空间冲突。</summary>
    private static void OnUi(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            action();
            return;
        }
        app.Dispatcher.Invoke(action);
    }
}
