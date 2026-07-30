using System.Windows;
using System.Windows.Threading;
using FuturesTrader.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// 窗口成组同步协调器：监听同组窗口的 <see cref="Window.LocationChanged"/> / <see cref="Window.SizeChanged"/>，
/// 节流 16ms（≈60fps）后批量同步其他窗口的位置/高度，实现 0527.exe 的成组拖动/缩放联动。
/// <para>
/// <b>防反馈环</b>：同步赋值时置 <c>_isUpdating=true</c>，跳过回调，避免 A→B→A 死循环。
/// </para>
/// <para>
/// <b>同步策略</b>：
/// <list type="bullet">
///   <item>拖动：同组窗口做相同位移（delta），保持相对排列不变</item>
///   <item>缩放：高度全组同步（对齐 0527.exe「窗口大小自动调整」），宽度各自独立（品种差异）</item>
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
            _groups.Remove(groupId);
    }

    private void OnLocationChanged(TrackedWindow source)
    {
        if (_isUpdating || SyncMode != WindowSyncMode.Grouped) return;

        var delta = new Vector(source.Window.Left - source.LastLeft, source.Window.Top - source.LastTop);
        if (delta.Length < 0.5) return;

        ScheduleSync(source, delta, syncHeight: false);
    }

    private void OnSizeChanged(TrackedWindow source)
    {
        if (_isUpdating || SyncMode != WindowSyncMode.Grouped) return;

        var heightDelta = source.Window.ActualHeight - source.LastHeight;
        if (Math.Abs(heightDelta) < 0.5) return;

        // 缩放仅同步高度（宽度各自独立）
        ScheduleSync(source, new Vector(0, 0), syncHeight: true);
    }

    private void ScheduleSync(TrackedWindow source, Vector delta, bool syncHeight)
    {
        // 节流：用 DispatcherTimer 16ms 合并高频事件，避免逐帧同步抖动
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ApplySync(source, delta, syncHeight);
        };
        timer.Start();
    }

    private void ApplySync(TrackedWindow source, Vector delta, bool syncHeight)
    {
        if (!_groups.TryGetValue(source.GroupId, out var list)) return;

        _isUpdating = true;
        try
        {
            foreach (var tracked in list)
            {
                if (tracked.Window == source.Window) continue;
                if (!tracked.Window.IsLoaded) continue;

                // 位移同步（拖动联动）
                if (Math.Abs(delta.X) > 0.5 || Math.Abs(delta.Y) > 0.5)
                {
                    tracked.Window.Left += delta.X;
                    tracked.Window.Top += delta.Y;
                }

                // 高度同步（缩放联动）
                if (syncHeight)
                {
                    tracked.Window.Height = source.Window.ActualHeight;
                }

                tracked.UpdatePosition();
            }

            // 更新源窗口的上次位置基线
            source.UpdatePosition();
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
        public double LastHeight { get; private set; } = window.ActualHeight;

        public void UpdatePosition()
        {
            LastLeft = Window.Left;
            LastTop = Window.Top;
            LastHeight = Window.ActualHeight;
        }
    }
}
