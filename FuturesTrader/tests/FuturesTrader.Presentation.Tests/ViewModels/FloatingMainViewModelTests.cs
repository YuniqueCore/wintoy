using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.WindowHosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="FloatingMainViewModel"/> 单元测试：覆盖派生属性 <see cref="FloatingMainViewModel.IsSyncGrouped"/>
/// 的双向同步语义（setter 写回 <c>SyncMode</c> + <c>GroupSynchronizationCoordinator.SyncMode</c>，
/// <c>SyncMode</c> 变更触发 <c>IsSyncGrouped</c> PropertyChanged 通知）。
/// <para>
/// 渲染层（StateToggleButton 实际切换可视化）由 UiAutomationTests 在真实 STA 线程上覆盖；
/// 本测试只覆盖数据层同步契约——StateToggleButton.IsChecked 的 TwoWay 绑定依赖此契约。
/// </para>
/// </summary>
public class FloatingMainViewModelTests
{
    private static FloatingMainViewModel CreateVm(
        StubSessionService? session = null,
        StubWindowGroupRepository? groupRepo = null,
        StubWindowHost? windowHost = null,
        GroupSynchronizationCoordinator? sync = null)
    {
        session ??= new StubSessionService();
        groupRepo ??= new StubWindowGroupRepository(new WindowLayout
        {
            UserId = "test",
            Windows = [],
            Groups = WindowLayout.CreateDefaultGroups()
        });
        windowHost ??= new StubWindowHost();
        sync ??= new GroupSynchronizationCoordinator(
            Options.Create(new UiOptions()),
            NullLogger<GroupSynchronizationCoordinator>.Instance);

        var groupService = new WindowGroupService(
            groupRepo,
            windowHost,
            Options.Create(new WindowLayoutOptions { UserId = "test" }),
            NullLogger<WindowGroupService>.Instance);

        return new FloatingMainViewModel(
            session,
            groupService,
            windowHost,
            sync,
            Options.Create(new UiOptions()),
            NullLogger<FloatingMainViewModel>.Instance,
            NullLoggerFactory.Instance);
    }

    // ── 默认状态 ───────────────────────────────────────

    [Fact]
    public void Default_IsSyncGrouped_is_true_when_SyncMode_is_Grouped()
    {
        var vm = CreateVm();

        // 构造期 SyncMode 默认值 = Grouped → IsSyncGrouped 应为 true，
        // 这样浮动栏启动时同步按钮就是 ON（Accent 填充）状态
        vm.SyncMode.Should().Be(WindowSyncMode.Grouped);
        vm.IsSyncGrouped.Should().BeTrue("默认 SyncMode=Grouped 应映射为 IsSyncGrouped=true");
    }

    // ── SyncMode 变化触发 IsSyncGrouped 通知 ──────────

    [Fact]
    public void SyncMode_change_raises_IsSyncGrouped_property_changed()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.SyncMode = WindowSyncMode.Independent;

        changes.Should().Contain(nameof(FloatingMainViewModel.IsSyncGrouped),
            "SyncMode 变更必须通知 IsSyncGrouped 重计算，让 StateToggleButton.IsChecked 双向绑定保持同步");
    }

    [Fact]
    public void Setting_SyncMode_to_Independent_updates_IsSyncGrouped_to_false()
    {
        var vm = CreateVm();

        vm.SyncMode = WindowSyncMode.Independent;

        vm.IsSyncGrouped.Should().BeFalse("SyncMode=Independent 应映射为 IsSyncGrouped=false");
    }

    // ── IsSyncGrouped setter 写回 ──────────────────────

    [Fact]
    public void Setting_IsSyncGrouped_false_updates_SyncMode_to_Independent()
    {
        var vm = CreateVm();

        vm.IsSyncGrouped = false;

        vm.SyncMode.Should().Be(WindowSyncMode.Independent,
            "setter false 应写回 SyncMode=Independent（让 ToggleSyncModeCommand 也能正确反映状态）");
    }

    [Fact]
    public void Setting_IsSyncGrouped_true_updates_SyncMode_to_Grouped()
    {
        var vm = CreateVm();
        vm.SyncMode = WindowSyncMode.Independent;

        vm.IsSyncGrouped = true;

        vm.SyncMode.Should().Be(WindowSyncMode.Grouped);
    }

    [Fact]
    public void Setting_IsSyncGrouped_propagates_to_GroupSynchronizationCoordinator()
    {
        var sync = new GroupSynchronizationCoordinator(
            Options.Create(new UiOptions()),
            NullLogger<GroupSynchronizationCoordinator>.Instance);
        var vm = CreateVm(sync: sync);
        sync.SyncMode.Should().Be(WindowSyncMode.Grouped, "coordinator 默认 Grouped");

        vm.IsSyncGrouped = false;

        sync.SyncMode.Should().Be(WindowSyncMode.Independent,
            "setter 必须同步到 coordinator，否则浮点窗口联动逻辑不会跟随切换");
    }

    [Fact]
    public void Setting_IsSyncGrouped_to_same_value_is_noop()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.IsSyncGrouped = true; // 已经是 true，无变化

        changes.Should().NotContain(nameof(FloatingMainViewModel.SyncMode),
            "setter 与当前值相同时不应触发 SyncMode 变更（避免无谓的写回 + coordinator 同步）");
    }

    // ── 切换命令 ──────────────────────────────────────

    [Fact]
    public void ToggleSyncModeCommand_flips_IsSyncGrouped()
    {
        var vm = CreateVm();
        var initial = vm.IsSyncGrouped;

        vm.ToggleSyncModeCommand.Execute(null);

        vm.IsSyncGrouped.Should().Be(!initial,
            "ToggleSyncModeCommand 应翻转 IsSyncGrouped（成组 ↔ 独立）");
    }

    // ── 桩实现 ────────────────────────────────────────

    /// <summary>测试用会话服务：所有属性返回 null（未登录），只暴露必要 getter。</summary>
    private sealed class StubSessionService : ISessionService
    {
        public SessionState CurrentState => new SessionState.Idle();
        public AccountEntry? Account => null;
        public IMarketDataService? MarketData => null;
        public ITradingService? Trading => null;
        public event EventHandler<SessionState>? StateChanged { add { } remove { } }
        public Task<SessionState> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionState>(new SessionState.Idle());
        public Task LogoutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>测试用窗口宿主桩：所有方法 no-op。</summary>
    private sealed class StubWindowHost : IWindowHost
    {
        public bool IsOpen(string instrumentCode) => false;
        public void Open(InstrumentWindow window) { }
        public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId) { }
        public IReadOnlyList<string> GetOpenWindowsInGroup(int groupId) => Array.Empty<string>();
        public void Focus(string instrumentCode) { }
        public void Close(string instrumentCode) { }
        public void CloseGroup(int groupId) { }
    }

    /// <summary>测试用窗口分组仓库桩：仅持有内存 WindowLayout。</summary>
    private sealed class StubWindowGroupRepository : IWindowGroupRepository
    {
        private readonly WindowLayout _layout;
        public StubWindowGroupRepository(WindowLayout layout) => _layout = layout;
        public WindowLayout Load(WindowLayoutOptions options) => _layout;
        public void Save(WindowLayoutOptions options, WindowLayout layout) { }
    }

    /// <summary>NullLoggerFactory 兼容垫片（MEL Extensions 8+ 已合并到 NullLogger.Instance）。</summary>
    private static class NullLoggerFactory
    {
        public static readonly Microsoft.Extensions.Logging.ILoggerFactory Instance =
            Microsoft.Extensions.Logging.LoggerFactory.Create(b => { });
    }
}
