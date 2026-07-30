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

        // 单组显示：先把其他分组的窗口隐藏（不关，保留 VM/状态/订阅），
        // 避免多组同屏混乱。对齐 0527.exe「点组号 → 只显该组」的语义。
        HideOtherGroups(groupId);

        // 强制重排当前组：先关掉当前已开的同组窗口（关事件会回写最新位置到 layout），
        // 再 OpenGroup 用布局中的位置紧排。下次切回该组时不会因上次拖动基线错位而重叠。
        CloseGroup(groupId);

        // 水平紧密排列 + 整组居中：计算总宽度后从屏幕工作区水平居中起始。
        // 关键修复：用 ClampWidth 强制最小宽度 320（默认 271 太小导致叠加时窗口收窄不可见），
        // 并用 Open 后的实际窗口宽度（ActualWidth）作为下一个窗口的左偏移计算依据，
        // 避免 DPI 缩放/Chrome 修饰导致 Width ≠ ActualWidth 引发的窗口重叠。
        var workArea = SystemParameters.WorkArea;
        var startY = workArea.Top + 8;
        var spacing = _uiOptions.CompactSpacing;
        var arranged = new List<InstrumentWindow>(windows.Count);

        // 第一遍：先算每个窗口应该摆放的 Left（用配置 Width + spacing，spacing=0 表示完全紧贴），
        // 避免窗口之间出现「边界缝隙」或「重叠 1 像素」。
        var arrangedWidths = windows.Select(w => Math.Max(w.Width, 320)).ToArray();
        var totalWidth = arrangedWidths.Sum() + Math.Max(0, windows.Count - 1) * spacing;
        var currentX = workArea.Left + Math.Max(8, (int)((workArea.Width - totalWidth) / 2));
        for (var i = 0; i < windows.Count; i++)
        {
            var w = windows[i];
            arranged.Add(w with { Left = (int)currentX, Top = (int)startY });
            currentX += arrangedWidths[i] + spacing;
        }

        // 第二遍：开窗（Open 内部用 arranged.Left/Top），开完后用 _open 中的实际窗口同步一次 Left，
        // 防止 tradingWindow 构造时 Width 设定后 chrome 调整让 ActualWidth 与预期不一致。
        for (var i = 0; i < arranged.Count; i++)
        {
            Open(arranged[i]);
        }

        // 第三遍：用实际渲染后的 ActualWidth 校正 Left（最关键的「无重叠」保证）。
        // 如果 ActualWidth < 预期宽度（窗口被压缩），左移后续窗口；反之亦然。
        // 用 synchronized 字典的引用顺序：按 Open 调用顺序遍历 _open 中当前组。
        TightenGroupLayout(groupId, spacing);

        _logger.LogInformation("已打开分组 {GroupId} 的 {Count} 个窗口（隐藏其他组 + 重排）",
            groupId, windows.Count);
    });

    /// <summary>
    /// 按当前 _open 字典的顺序，重新计算同组窗口的 Left，确保相邻窗口 ActualWidth 紧贴 + spacing，
    /// 避免 Open 时 Width 设置与实际渲染后 ActualWidth 不一致导致的 1-2 像素重叠或缝隙。
    /// 复刻 0527.exe「窗口边缘紧贴」语义。
    /// </summary>
    private void TightenGroupLayout(int groupId, int spacing)
    {
        List<Window> sameGroup;
        lock (_open)
        {
            sameGroup = _open
                .Where(kv => kv.Value.GroupId == groupId)
                .Select(kv => kv.Value.Window)
                .ToList();
        }
        if (sameGroup.Count <= 1) return;

        // 按当前 Left 升序排列 → 从左到右依次重排
        sameGroup.Sort((a, b) => a.Left.CompareTo(b.Left));

        // 第一窗口：从它的 Left 开始
        var currentLeft = sameGroup[0].Left;
        sameGroup[0].Left = currentLeft;
        for (var i = 1; i < sameGroup.Count; i++)
        {
            var prev = sameGroup[i - 1];
            var expectedNextLeft = prev.Left + prev.ActualWidth + spacing;
            // 只有当现有 Left 与期望差距 > 0.5 像素时才校正（避免抖动）
            if (Math.Abs(sameGroup[i].Left - expectedNextLeft) > 0.5)
            {
                sameGroup[i].Left = expectedNextLeft;
            }
            currentLeft = sameGroup[i].Left;
        }
    }

    /// <summary>隐藏所有非 <paramref name="keepGroupId"/> 分组的窗口（不关，保留 VM 状态）。</summary>
    private int HideOtherGroups(int keepGroupId)
    {
        List<Window> toHide;
        lock (_open)
        {
            toHide = _open
                .Where(kv => kv.Value.GroupId != keepGroupId)
                .Select(kv => kv.Value.Window)
                .ToList();
        }
        foreach (var w in toHide)
        {
            try { w.Hide(); }
            catch (Exception ex) { _logger.LogWarning(ex, "隐藏窗口失败"); }
        }
        if (toHide.Count > 0)
            _logger.LogDebug("已隐藏 {Count} 个非同组窗口（保留组 {Keep}）", toHide.Count, keepGroupId);
        return toHide.Count;
    }

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
