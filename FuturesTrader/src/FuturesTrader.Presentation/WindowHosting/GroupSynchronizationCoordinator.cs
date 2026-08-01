using System.Windows;
using System.Windows.Threading;
using FuturesTrader.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// 窗口成组同步协调器：监听同组窗口的 <see cref="Window.LocationChanged"/> / <see cref="Window.SizeChanged"/>，
/// 防抖 16ms 后读取用户操作窗口的最新最终坐标，批量同步其他窗口的位置/高度。
/// <para>
/// <b>防反馈环</b>：同步赋值时置 <c>_isUpdating=true</c>，跳过回调，避免 A→B→A 死循环。
/// </para>
/// <para>
/// <b>同步策略</b>：
/// <list type="bullet">
///   <item>拖动：以源窗口为锚点，全部窗口 Top 对齐，按注册顺序向锚点两侧紧密排列</item>
///   <item>缩放：高度全组同步，宽度保持各窗独立；随后按实际宽度重新排列，保证不重叠</item>
/// </list>
/// </para>
/// <para>
/// <b>持久化节流</b>：位置/大小变更不每帧写盘，仅 <see cref="Unregister"/> 时由调用方触发保存。
/// </para>
/// </summary>
public sealed class GroupSynchronizationCoordinator
{
    private readonly ILogger<GroupSynchronizationCoordinator> _logger;
    private readonly UiOptions _uiOptions;

    /// <summary>同组窗口注册表：groupId → 窗口集合。同组窗口联动。</summary>
    private readonly Dictionary<int, List<TrackedWindow>> _groups = new();

    /// <summary>每组最多一个待执行同步；连续拖动只保留最新锚点坐标，避免旧 delta 定时器乱序回放。</summary>
    private readonly Dictionary<int, PendingSync> _pendingSyncs = new();

    /// <summary>防反馈环标志：批量同步赋值期间为 true，跳过回调。</summary>
    private bool _isUpdating;

    /// <summary>当前同步模式（Grouped 联动 / Independent 独立）。</summary>
    public WindowSyncMode SyncMode { get; set; } = WindowSyncMode.Grouped;

    public GroupSynchronizationCoordinator(
        IOptions<UiOptions> uiOptions,
        ILogger<GroupSynchronizationCoordinator> logger)
    {
        _uiOptions = uiOptions.Value;
        _logger = logger;
    }

    /// <summary>注册窗口到分组，挂载 LocationChanged/SizeChanged 监听。</summary>
    public void Register(Window window, int groupId)
    {
        if (groupId < 1 || groupId > 20) return;

        if (!_groups.TryGetValue(groupId, out var list))
        {
            list = new List<TrackedWindow>();
            _groups[groupId] = list;
        }

        // 记录上次位置用于计算位移 delta
        var tracked = new TrackedWindow(window, groupId);
        list.Add(tracked);

        window.LocationChanged += (_, _) => OnLocationChanged(tracked);
        window.SizeChanged += (_, _) => OnSizeChanged(tracked);
        _logger.LogDebug("窗口 {Handle} 已注册到分组 {GroupId}", window.GetHashCode(), groupId);
    }

    /// <summary>从分组注销（窗口关闭时调用）。</summary>
    public void Unregister(Window window, int groupId)
    {
        if (!_groups.TryGetValue(groupId, out var list)) return;
        list.RemoveAll(t => t.Window == window);
        if (list.Count == 0)
        {
            _groups.Remove(groupId);
            if (_pendingSyncs.Remove(groupId, out var pending))
                pending.Timer.Stop();
        }
    }

    private void OnLocationChanged(TrackedWindow source)
    {
        if (_isUpdating || SyncMode != WindowSyncMode.Grouped) return;

        var leftChanged = Math.Abs(source.Window.Left - source.LastLeft) >= 0.5;
        var topChanged = Math.Abs(source.Window.Top - source.LastTop) >= 0.5;
        if (!leftChanged && !topChanged) return;

        ScheduleSync(source, syncHeight: false);
    }

    private void OnSizeChanged(TrackedWindow source)
    {
        if (_isUpdating || SyncMode != WindowSyncMode.Grouped) return;

        var heightDelta = source.Window.ActualHeight - source.LastHeight;
        var widthDelta = source.Window.ActualWidth - source.LastWidth;
        if (Math.Abs(heightDelta) < 0.5 && Math.Abs(widthDelta) < 0.5) return;

        // 高度同步，宽度可不同，但之后会按实际宽度重新贴边。
        ScheduleSync(source, syncHeight: true);
    }

    private void ScheduleSync(TrackedWindow source, bool syncHeight)
    {
        if (_pendingSyncs.TryGetValue(source.GroupId, out var existing))
        {
            existing.Source = source;
            existing.SyncHeight |= syncHeight;
            existing.Timer.Stop();
            existing.Timer.Start();
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        var pending = new PendingSync(timer, source, syncHeight);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _pendingSyncs.Remove(source.GroupId);
            ApplySync(pending.Source, pending.SyncHeight);
        };
        _pendingSyncs[source.GroupId] = pending;
        timer.Start();
    }

    private void ApplySync(TrackedWindow source, bool syncHeight)
    {
        if (SyncMode != WindowSyncMode.Grouped) return;
        if (!_groups.TryGetValue(source.GroupId, out var list)) return;

        var trackedWindows = list.Where(tracked => tracked.Window.IsLoaded).ToArray();
        var sourceIndex = Array.IndexOf(trackedWindows, source);
        if (sourceIndex < 0) return;

        _isUpdating = true;
        try
        {
            if (syncHeight)
            {
                var sourceHeight = ResolveHeight(source.Window);
                foreach (var tracked in trackedWindows)
                    tracked.Window.Height = sourceHeight;
            }

            var bounds = trackedWindows
                .Select(tracked => new WindowBounds(
                    tracked.Window.Left,
                    tracked.Window.Top,
                    ResolveWidth(tracked.Window),
                    ResolveHeight(tracked.Window)))
                .ToArray();
            var placements = CalculateAlignedLayout(
                bounds,
                sourceIndex,
                _uiOptions.CompactSpacing,
                SystemParameters.WorkArea);

            for (var index = 0; index < trackedWindows.Length; index++)
            {
                trackedWindows[index].Window.Left = placements[index].Left;
                trackedWindows[index].Window.Top = placements[index].Top;
                trackedWindows[index].UpdatePosition();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "窗口同步异常（组 {GroupId}）", source.GroupId);
        }
        finally
        {
            _isUpdating = false;
        }
    }

    /// <summary>
    /// 以锚点窗口当前 Left/Top 计算整组最终位置。锚点两侧按顺序紧贴；整行能放入工作区时整体钳制，
    /// 既不破坏相邻窗口间距，也不会为了对齐而制造重叠。
    /// </summary>
    internal static IReadOnlyList<WindowPlacement> CalculateAlignedLayout(
        IReadOnlyList<WindowBounds> windows,
        int anchorIndex,
        double spacing,
        Rect workArea)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (windows.Count == 0) return Array.Empty<WindowPlacement>();
        if (anchorIndex < 0 || anchorIndex >= windows.Count)
            throw new ArgumentOutOfRangeException(nameof(anchorIndex));

        spacing = Math.Max(0, spacing);
        var widths = windows.Select(window => Math.Max(1, window.Width)).ToArray();
        var lefts = new double[windows.Count];
        lefts[anchorIndex] = windows[anchorIndex].Left;

        for (var index = anchorIndex - 1; index >= 0; index--)
            lefts[index] = lefts[index + 1] - spacing - widths[index];
        for (var index = anchorIndex + 1; index < windows.Count; index++)
            lefts[index] = lefts[index - 1] + widths[index - 1] + spacing;

        if (!workArea.IsEmpty && workArea.Width > 0)
        {
            var rowWidth = widths.Sum() + spacing * Math.Max(0, windows.Count - 1);
            var rowRight = lefts[^1] + widths[^1];
            var shift = rowWidth > workArea.Width
                ? workArea.Left - lefts[0]
                : lefts[0] < workArea.Left
                    ? workArea.Left - lefts[0]
                    : rowRight > workArea.Right
                        ? workArea.Right - rowRight
                        : 0;
            if (Math.Abs(shift) >= 0.5)
            {
                for (var index = 0; index < lefts.Length; index++)
                    lefts[index] += shift;
            }
        }

        var maxHeight = windows.Max(window => Math.Max(1, window.Height));
        var targetTop = windows[anchorIndex].Top;
        if (!workArea.IsEmpty && workArea.Height > 0)
            targetTop = Math.Clamp(targetTop, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - maxHeight));

        return lefts.Select(left => new WindowPlacement(left, targetTop)).ToArray();
    }

    private static double ResolveWidth(Window window) =>
        window.ActualWidth > 0 && double.IsFinite(window.ActualWidth)
            ? window.ActualWidth
            : Math.Max(1, window.Width);

    private static double ResolveHeight(Window window) =>
        window.ActualHeight > 0 && double.IsFinite(window.ActualHeight)
            ? window.ActualHeight
            : Math.Max(1, window.Height);

    /// <summary>获取指定分组已注册的窗口（供 WindowManager 持久化位置）。</summary>
    public IReadOnlyList<Window> GetWindowsInGroup(int groupId)
    {
        return _groups.TryGetValue(groupId, out var list)
            ? list.Select(t => t.Window).ToArray()
            : Array.Empty<Window>();
    }

    /// <summary>已注册窗口 + 上次位置记录。</summary>
    private sealed class TrackedWindow(Window window, int groupId)
    {
        public Window Window { get; } = window;
        public int GroupId { get; } = groupId;
        public double LastLeft { get; private set; } = window.Left;
        public double LastTop { get; private set; } = window.Top;
        public double LastWidth { get; private set; } = window.ActualWidth;
        public double LastHeight { get; private set; } = window.ActualHeight;

        public void UpdatePosition()
        {
            LastLeft = Window.Left;
            LastTop = Window.Top;
            LastWidth = Window.ActualWidth;
            LastHeight = Window.ActualHeight;
        }
    }

    private sealed class PendingSync(DispatcherTimer timer, TrackedWindow source, bool syncHeight)
    {
        public DispatcherTimer Timer { get; } = timer;
        public TrackedWindow Source { get; set; } = source;
        public bool SyncHeight { get; set; } = syncHeight;
    }

    internal readonly record struct WindowBounds(double Left, double Top, double Width, double Height);

    internal readonly record struct WindowPlacement(double Left, double Top);
}
