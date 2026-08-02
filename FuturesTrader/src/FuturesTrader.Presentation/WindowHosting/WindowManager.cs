using System.Windows;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;
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
/// <see cref="OpenGroup"/>：隐藏其他组、恢复已有实例或创建缺失实例，再水平紧密排列（无重叠：window.Left = prev.Right + CompactSpacing）。
/// </para>
/// <para>
/// 全部 <see cref="Dispatcher.Invoke"/> 包裹：MCP HTTP 线程触发时 marshalled 到 UI 线程，避免跨线程异常。
/// TradingWindow 关闭时由其 OnClosing Dispose ViewModel（退订行情），Closed 事件移除字典项 + 注销同步。
/// </para>
/// </summary>
public sealed class WindowManager : IWindowHost, ITradingWindowInteractionService
{
    private readonly IServiceProvider _services;
    private readonly ISessionService _session;
    private readonly IKeyboardOperationService _keyboard;
    private readonly IGlobalOrderCancellationService _globalCancellation;
    private readonly GroupSynchronizationCoordinator _sync;
    private readonly IWindowGroupRepository _windowGroupRepository;
    private readonly IConfigRepository _configRepository;
    private readonly WindowLayoutOptions _windowLayoutOptions;
    private readonly ConfigFileOptions _configFileOptions;
    private readonly UiOptions _uiOptions;
    private readonly ILogger<WindowManager> _logger;

    /// <summary>已打开窗口字典：合约码 → 窗口 + 分组号。用于 Open 去重 + CloseGroup 索引。</summary>
    private readonly Dictionary<string, TrackedOpen> _open = new(StringComparer.Ordinal);
    private bool _showWhiteGrid = true;

    public WindowManager(
        IServiceProvider services,
        ISessionService session,
        IKeyboardOperationService keyboard,
        IGlobalOrderCancellationService globalCancellation,
        GroupSynchronizationCoordinator sync,
        IWindowGroupRepository windowGroupRepository,
        IConfigRepository configRepository,
        IOptions<WindowLayoutOptions> windowLayoutOptions,
        IOptions<ConfigFileOptions> configFileOptions,
        IOptions<UiOptions> uiOptions,
        ILogger<WindowManager> logger)
    {
        _services = services;
        _session = session;
        _keyboard = keyboard;
        _globalCancellation = globalCancellation;
        _sync = sync;
        _windowGroupRepository = windowGroupRepository;
        _configRepository = configRepository;
        _windowLayoutOptions = windowLayoutOptions.Value;
        _configFileOptions = configFileOptions.Value;
        _uiOptions = uiOptions.Value;
        _logger = logger;
    }

    private sealed record TrackedOpen(
        Window Window,
        int GroupId,
        TradingViewModel ViewModel);

    /// <inheritdoc />
    public bool IsOpen(string instrumentCode)
    {
        ArgumentNullException.ThrowIfNull(instrumentCode);
        lock (_open) return _open.ContainsKey(instrumentCode);
    }

    /// <inheritdoc />
    public void ApplyOnlyOpenToOpenWindows(bool onlyOpen) => OnUi(() =>
    {
        var viewModels = SnapshotOpenViewModels();
        foreach (var viewModel in viewModels)
            viewModel.CbOnlyOpen = onlyOpen;
        _logger.LogInformation("浮动栏仓平模式已应用到 {Count} 个已创建合约窗口：OnlyOpen={OnlyOpen}",
            viewModels.Count, onlyOpen);
    });

    /// <inheritdoc />
    public void ApplyOrderPlacementModeToOpenWindows(OrderPlacementMode placementMode) => OnUi(() =>
    {
        var viewModels = SnapshotOpenViewModels();
        foreach (var viewModel in viewModels)
            viewModel.OrderPlacementMode = placementMode;
        _logger.LogInformation("浮动栏 A/B 模式已应用到 {Count} 个已创建合约窗口：{Mode}",
            viewModels.Count, placementMode);
    });

    /// <inheritdoc />
    public void ApplyWhiteGridVisibilityToOpenWindows(bool showWhiteGrid) => OnUi(() =>
    {
        _showWhiteGrid = showWhiteGrid;
        var viewModels = SnapshotOpenViewModels();
        foreach (var viewModel in viewModels)
            viewModel.ShowWhiteGrid = showWhiteGrid;
        _logger.LogInformation("浮动栏白格显示已应用到 {Count} 个已创建合约窗口：Show={Show}",
            viewModels.Count, showWhiteGrid);
    });

    /// <inheritdoc />
    public void ApplyWindowDisplayConfigurationToOpenWindows(WindowConfig configuration) => OnUi(() =>
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var viewModels = SnapshotOpenViewModels();
        foreach (var viewModel in viewModels)
        {
            viewModel.RowHeight = Math.Clamp(configuration.TickRowHeights, 10, 32);
            viewModel.AskQuoteRowCount = Math.Clamp(configuration.AskQuoteRowCount, 5, 100);
            viewModel.BidQuoteRowCount = Math.Clamp(configuration.BidQuoteRowCount, 5, 100);
        }
        _logger.LogInformation(
            "共享合约窗口显示配置已应用到 {Count} 个窗口：RowHeight={RowHeight} AskRows={AskRows} BidRows={BidRows}",
            viewModels.Count,
            configuration.TickRowHeights,
            configuration.AskQuoteRowCount,
            configuration.BidQuoteRowCount);
    });

    /// <inheritdoc />
    public void RecenterVisiblePriceLadders(PriceLadderAnchor anchor) => OnUi(() =>
    {
        List<TradingWindow> visible;
        lock (_open)
        {
            visible = _open.Values
                .Select(tracked => tracked.Window)
                .OfType<TradingWindow>()
                .Where(window => window.IsVisible)
                .ToList();
        }
        foreach (var window in visible) window.RecenterPriceLadder(anchor);
        _logger.LogDebug("已将 {Count} 个可见价格梯定位到 {Anchor}", visible.Count, anchor);
    });

    /// <inheritdoc />
    public void Open(InstrumentWindow window) => OnUi(() =>
    {
        ArgumentNullException.ThrowIfNull(window);
        if (string.IsNullOrWhiteSpace(window.InstrumentCode)) return;

        lock (_open)
        {
            if (_open.TryGetValue(window.InstrumentCode, out var existing))
            {
                MoveTrackedWindowToGroupIfNeeded(window.InstrumentCode, existing, window.GroupId);
                if (!existing.Window.IsVisible)
                    existing.Window.Show();
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

        var sharedDisplay = LoadWindowConfiguration();
        var effectiveWindow = window with
        {
            RowHeight = Math.Clamp(sharedDisplay.TickRowHeights, 10, 32),
            AskQuoteRowCount = Math.Clamp(sharedDisplay.AskQuoteRowCount, 5, 100),
            BidQuoteRowCount = Math.Clamp(sharedDisplay.BidQuoteRowCount, 5, 100),
        };
        var vm = (TradingViewModel)ActivatorUtilities.CreateInstance(
            _services, typeof(TradingViewModel), effectiveWindow, marketData, trading);
        vm.ShowWhiteGrid = _showWhiteGrid;

        // 新窗口（Left/Top=0 未设置位置）追加到同组已有窗口的最右侧对齐
        var (left, top) = (window.Left, window.Top);
        if (left == 0 && top == 0)
        {
            (left, top) = ComputeAppendPosition(window.GroupId, Math.Max(window.Width, 320));
        }

        var tradingWindow = new TradingWindow(_keyboard, _globalCancellation, this)
        {
            Width = Math.Max(window.Width, 320),
            Height = Math.Max(window.Height, 480),
            Left = left,
            Top = top,
            DataContext = vm
        };

        var groupId = window.GroupId;
        var cancellationRegistration = _globalCancellation.Register(
            vm.CancelAllOrdersAsync,
            () => tradingWindow.IsVisible);
        tradingWindow.Closed += (_, _) =>
        {
            // 窗口关闭时将 VM 的窗口级状态合并回 Users.xml，不能只计算后丢弃。
            try
            {
                var registeredGroupId = groupId;
                lock (_open)
                {
                    if (_open.Remove(window.InstrumentCode, out var tracked))
                        registeredGroupId = tracked.GroupId;
                }
                var updated = vm.ToInstrumentWindow() with
                {
                    GroupId = registeredGroupId,
                    Top = (int)Math.Round(tradingWindow.Top),
                    Left = (int)Math.Round(tradingWindow.Left),
                    Height = (int)Math.Round(tradingWindow.Height),
                    Width = (int)Math.Round(tradingWindow.Width)
                };
                _sync.Unregister(tradingWindow, registeredGroupId);
                cancellationRegistration.Dispose();
                PersistWindowConfiguration(updated);
                _logger.LogInformation("合约窗口已关闭: {Instrument}", window.InstrumentCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "回写窗口配置失败: {Instrument}", window.InstrumentCode);
            }
        };

        lock (_open) _open[window.InstrumentCode] = new TrackedOpen(tradingWindow, groupId, vm);
        _sync.Register(tradingWindow, groupId);
        tradingWindow.Show();
        _logger.LogInformation("合约窗口已打开: {Instrument} (组 {Group})", window.InstrumentCode, groupId);
    });

    /// <inheritdoc />
    public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId) => OnUi(() =>
    {
        // 空组同样代表一次有效的组切换：必须先隐藏其他组，再显示“无合约窗口”的目标状态。
        HideOtherGroups(groupId);
        if (windows.Count == 0)
        {
            _logger.LogInformation("分组 {GroupId} 无窗口", groupId);
            return;
        }

        // 单组显示：先把其他分组的窗口隐藏（不关，保留 VM/状态/订阅），
        // 避免多组同屏混乱。对齐 0527.exe「点组号 → 只显该组」的语义。
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

        // 第二遍：恢复已有实例，或只为不存在的合约创建新窗。切组不得 Close/Recreate，
        // 否则会丢失订单生命周期、行情订阅和窗口内临时状态。
        foreach (var arrangedWindow in arranged)
        {
            if (RestoreOpenWindow(arrangedWindow))
                continue;
            Open(arrangedWindow);
        }

        // 第三遍：用实际渲染后的 ActualWidth 校正 Left（最关键的「无重叠」保证）。
        // 如果 ActualWidth < 预期宽度（窗口被压缩），左移后续窗口；反之亦然。
        // 以布局文件中的分组顺序为准，不依赖 Dictionary 枚举顺序。
        TightenGroupLayout(groupId, spacing, arranged.Select(window => window.InstrumentCode).ToArray());

        _logger.LogInformation("已恢复分组 {GroupId} 的 {Count} 个窗口（隐藏其他组 + 重排）",
            groupId, windows.Count);
    });

    private WindowConfig LoadWindowConfiguration()
    {
        try
        {
            return _configRepository.Load(_configFileOptions.Path).Window;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取共享合约窗口显示配置失败，使用安全默认值");
            return new WindowConfig();
        }
    }

    /// <summary>恢复已有窗口时仅更新几何和可见性，不重新构造其 ViewModel。</summary>
    private bool RestoreOpenWindow(InstrumentWindow config)
    {
        TrackedOpen? existing;
        lock (_open) _open.TryGetValue(config.InstrumentCode, out existing);
        if (existing is null) return false;

        MoveTrackedWindowToGroupIfNeeded(config.InstrumentCode, existing, config.GroupId);
        var window = existing.Window;
        window.Width = Math.Max(config.Width, 320);
        window.Height = Math.Max(config.Height, 480);
        window.Left = config.Left;
        window.Top = config.Top;
        if (!window.IsVisible) window.Show();
        return true;
    }

    /// <summary>合约重新分组时保留原窗口和 ViewModel，只更新组注册表。</summary>
    private void MoveTrackedWindowToGroupIfNeeded(string instrumentCode, TrackedOpen existing, int targetGroupId)
    {
        if (existing.GroupId == targetGroupId) return;
        _sync.Unregister(existing.Window, existing.GroupId);
        _sync.Register(existing.Window, targetGroupId);
        lock (_open) _open[instrumentCode] = existing with { GroupId = targetGroupId };
    }

    /// <summary>取活动 VM 快照后再执行属性更新，避免持锁调用绑定/通知代码。</summary>
    private IReadOnlyList<TradingViewModel> SnapshotOpenViewModels()
    {
        lock (_open)
            return _open.Values.Select(tracked => tracked.ViewModel).ToArray();
    }

    /// <summary>
    /// 将单窗回写与最新持久化布局合并，避免浮动栏持有的旧 WindowLayout 快照覆盖刚关闭窗口的 A/B 或仓平设置。
    /// </summary>
    private void PersistWindowConfiguration(InstrumentWindow updated)
    {
        var layout = _windowGroupRepository.Load(_windowLayoutOptions);
        var exists = layout.Windows.Any(window =>
            string.Equals(window.InstrumentCode, updated.InstrumentCode, StringComparison.Ordinal));
        var windows = layout.Windows
            .Select(window => string.Equals(window.InstrumentCode, updated.InstrumentCode, StringComparison.Ordinal)
                ? updated
                : window)
            .ToArray();
        if (!exists)
            windows = windows.Append(updated).ToArray();

        _windowGroupRepository.Save(_windowLayoutOptions, layout with { Windows = windows });
    }

    /// <summary>
    /// 重新计算同组窗口的 Left，确保相邻窗口 ActualWidth 紧贴 + spacing。
    /// 传入顺序时严格采用布局文件顺序；否则以当前 Left 为顺序。
    /// </summary>
    private void TightenGroupLayout(int groupId, int spacing, IReadOnlyList<string>? orderedInstrumentCodes = null)
    {
        List<(string InstrumentCode, Window Window)> sameGroup;
        lock (_open)
        {
            sameGroup = _open
                .Where(pair => pair.Value.GroupId == groupId)
                .Select(pair => (pair.Key, pair.Value.Window))
                .ToList();
        }
        if (sameGroup.Count <= 1) return;

        if (orderedInstrumentCodes is not null)
        {
            var order = orderedInstrumentCodes
                .Select((code, index) => (code, index))
                .ToDictionary(item => item.code, item => item.index, StringComparer.Ordinal);
            sameGroup.Sort((left, right) =>
                order.GetValueOrDefault(left.InstrumentCode, int.MaxValue)
                    .CompareTo(order.GetValueOrDefault(right.InstrumentCode, int.MaxValue)));
        }
        else
        {
            sameGroup.Sort((left, right) => left.Window.Left.CompareTo(right.Window.Left));
        }

        for (var i = 1; i < sameGroup.Count; i++)
        {
            var previous = sameGroup[i - 1].Window;
            var current = sameGroup[i].Window;
            var previousWidth = previous.ActualWidth > 0 ? previous.ActualWidth : previous.Width;
            var expectedNextLeft = previous.Left + previousWidth + spacing;
            if (Math.Abs(current.Left - expectedNextLeft) > 0.5)
                current.Left = expectedNextLeft;
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
    public void HideGroup(int groupId) => OnUi(() =>
    {
        List<Window> toHide;
        lock (_open)
        {
            toHide = _open.Values
                .Where(tracked => tracked.GroupId == groupId)
                .Select(tracked => tracked.Window)
                .ToList();
        }

        foreach (var window in toHide)
        {
            if (window.IsVisible) window.Hide();
        }

        _logger.LogInformation("已隐藏分组 {GroupId} 的 {Count} 个窗口（实例仍存活）", groupId, toHide.Count);
    });

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
