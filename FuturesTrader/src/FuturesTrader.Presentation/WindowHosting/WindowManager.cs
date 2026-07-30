using System.Windows;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// <see cref="IWindowHost"/> 的真实实现：用 <see cref="TradingWindow"/>（TYYWin 复刻）管理合约窗口。
/// <para>
/// <see cref="Open"/>：已开则 <c>Activate</c>；否则构造 <see cref="TradingViewModel"/> → <see cref="TradingWindow"/> → 记入字典 → <c>Show()</c>。
/// <see cref="OpenGroup"/>：循环 <see cref="Open"/> + 水平紧密排列（无重叠：window.Left = prev.Right + CompactSpacing）+ 注册到 <see cref="GroupSynchronizationCoordinator"/>。
/// </para>
/// <para>
/// 全部 <see cref="Dispatcher.Invoke"/> 包裹：MCP HTTP 线程触发时 marshalled 到 UI 线程，避免跨线程异常。
/// TradingWindow 关闭时由其 OnClosing Dispose ViewModel（退订行情），Closed 事件移除字典项 + 注销同步。
/// </para>
/// </summary>
public sealed class WindowManager : IWindowHost
{
    private readonly IServiceProvider _services;
    private readonly ISessionService _session;
    private readonly IKeyboardOperationService _keyboard;
    private readonly GroupSynchronizationCoordinator _sync;
    private readonly UiOptions _uiOptions;
    private readonly ILogger<WindowManager> _logger;

    /// <summary>已打开窗口字典：合约码 → 窗口 + 分组号。用于 Open 去重 + CloseGroup 索引。</summary>
    private readonly Dictionary<string, TrackedOpen> _open = new(StringComparer.Ordinal);

    public WindowManager(
        IServiceProvider services,
        ISessionService session,
        IKeyboardOperationService keyboard,
        GroupSynchronizationCoordinator sync,
        IOptions<UiOptions> uiOptions,
        ILogger<WindowManager> logger)
    {
        _services = services;
        _session = session;
        _keyboard = keyboard;
        _sync = sync;
        _uiOptions = uiOptions.Value;
        _logger = logger;
    }

    private sealed record TrackedOpen(Window Window, int GroupId);

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
                existing.Window.Activate();
                return;
            }
        }

        // IMarketDataService / ITradingService 是会话级实例（由 SessionService 持有，不在 DI 中）。
        // 必须显式传递给 ActivatorUtilities，否则无法解析。
        var marketData = _session.MarketData
            ?? throw new InvalidOperationException("行情服务未初始化（未登录或已登出）");
        var trading = _session.Trading
            ?? throw new InvalidOperationException("交易服务未初始化（未登录或已登出）");

        var vm = (TradingViewModel)ActivatorUtilities.CreateInstance(
            _services, typeof(TradingViewModel), window, marketData, trading);

        // 新窗口（Left/Top=0 未设置位置）追加到同组已有窗口的最右侧对齐
        var (left, top) = (window.Left, window.Top);
        if (left == 0 && top == 0)
        {
            (left, top) = ComputeAppendPosition(window.GroupId, Math.Max(window.Width, 320));
        }

        var tradingWindow = new TradingWindow(_keyboard)
        {
            Title = BuildTitle(window),
            Width = Math.Max(window.Width, 320),
            Height = Math.Max(window.Height, 480),
            Left = left,
            Top = top,
            DataContext = vm
        };

        var groupId = window.GroupId;
        tradingWindow.Closed += (_, _) =>
        {
            // 窗口关闭时回写 33 字段配置（供后续持久化到 Users.xml）
            try
            {
                var updated = vm.ToInstrumentWindow();
                lock (_open) _open.Remove(window.InstrumentCode);
                _sync.Unregister(tradingWindow, groupId);
                _logger.LogInformation("合约窗口已关闭: {Instrument}", window.InstrumentCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回写窗口配置失败: {Instrument}", window.InstrumentCode);
            }
        };

        lock (_open) _open[window.InstrumentCode] = new TrackedOpen(tradingWindow, groupId);
        _sync.Register(tradingWindow, groupId);
        tradingWindow.Show();
        _logger.LogInformation("合约窗口已打开: {Instrument} (组 {Group})", window.InstrumentCode, groupId);
    });

    /// <inheritdoc />
    public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId) => OnUi(() =>
    {
        if (windows.Count == 0)
        {
            _logger.LogInformation("分组 {GroupId} 无窗口", groupId);
            return;
        }

        // 水平紧密排列 + 整组居中：计算总宽度后从屏幕工作区水平居中起始
        var workArea = SystemParameters.WorkArea;
        var startY = workArea.Top + 8;
        var spacing = _uiOptions.CompactSpacing;
        var windowWidths = windows.Select(w => Math.Max(w.Width, 320)).ToArray();
        var totalWidth = windowWidths.Sum() + (windows.Count - 1) * spacing;
        var currentX = workArea.Left + Math.Max(8, (workArea.Width - totalWidth) / 2);

        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            // 首次打开的窗口按紧密排列设置 Left/Top；已打开的保持原位
            if (!IsOpen(w.InstrumentCode))
            {
                var arranged = w with { Left = (int)currentX, Top = (int)startY };
                currentX += windowWidths[i] + spacing;
                Open(arranged);
            }
            else
            {
                Open(w);
            }
        }

        _logger.LogInformation("已打开分组 {GroupId} 的 {Count} 个窗口（水平紧密排列）",
            groupId, windows.Count);
    });

    /// <inheritdoc />
    public IReadOnlyList<string> GetOpenWindowsInGroup(int groupId)
    {
        lock (_open)
        {
            return _open
                .Where(kv => kv.Value.GroupId == groupId)
                .Select(kv => kv.Key)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public void Focus(string instrumentCode) => OnUi(() =>
    {
        lock (_open)
        {
            if (_open.TryGetValue(instrumentCode, out var t))
                t.Window.Activate();
        }
    });

    /// <inheritdoc />
    public void Close(string instrumentCode) => OnUi(() =>
    {
        lock (_open)
        {
            if (_open.TryGetValue(instrumentCode, out var t))
                t.Window.Close();
        }
    });

    /// <inheritdoc />
    public void CloseGroup(int groupId) => OnUi(() =>
    {
        List<Window> toClose;
        lock (_open)
        {
            toClose = _open
                .Where(kv => kv.Value.GroupId == groupId)
                .Select(kv => kv.Value.Window)
                .ToList();
        }
        foreach (var w in toClose) w.Close();
        _logger.LogInformation("已关闭分组 {GroupId} 的 {Count} 个窗口", groupId, toClose.Count);
    });

    /// <summary>计算新窗口追加位置：同组已打开窗口的最右侧；无同组窗口则从屏幕左侧开始。</summary>
    private (int Left, int Top) ComputeAppendPosition(int groupId, double newWindowWidth)
    {
        var workArea = SystemParameters.WorkArea;
        var spacing = _uiOptions.CompactSpacing;
        lock (_open)
        {
            var rightmost = _open
                .Where(kv => kv.Value.GroupId == groupId)
                .Select(kv => kv.Value.Window.Left + kv.Value.Window.Width)
                .DefaultIfEmpty(workArea.Left + 8 - spacing)
                .Max();
            return ((int)(rightmost + spacing), (int)workArea.Top + 8);
        }
    }

    /// <summary>构造窗口标题：合约码 · 组号（期权场景由 ContractWindowViewModel M4-C 扩展持续时间）。</summary>
    private static string BuildTitle(InstrumentWindow window)
    {
        return $"{window.InstrumentCode} · 组 {window.GroupId}";
    }

    /// <summary>在 UI 线程执行；无 WPF 应用上下文时（如单元测试）直接内联执行。</summary>
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
