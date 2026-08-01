using System.ComponentModel;
using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.Services;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Wpf.Ui.Appearance;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="SettingsViewModel"/> 单元测试：覆盖主题切换（外观段）、段落切换、Load/Save 编排。
/// 用真实 <see cref="ThemeService"/>（包装 WPF UI ApplicationThemeManager，无 WPF Application 时不抛异常）+
/// 内存仓库 + StubWindowHost 端到端验证，不引入 mock 框架。
/// </summary>
public class SettingsViewModelTests
{
    private static CloudConfig SeedConfig => new()
    {
        Window = new WindowConfig { MainFont = "Segoe UI", CompactSpacing = 1 },
        Order = new OrderConfig { RiskOpen = true, MaxInputCount = 10 },
        User = new UserConfig { HqAddress = "tcp://127.0.0.1:1234" }
    };

    private static SettingsViewModel CreateVm(
        CloudConfig? seedConfig = null,
        IThemeService? theme = null)
    {
        var repo = new InMemoryConfigRepository(seedConfig ?? SeedConfig);
        var options = Options.Create(new ConfigFileOptions { Path = "test.ini" });
        var dataOptions = Options.Create(new DataFileOptions { UsersXml = "test_users.xml" });
        var groupService = new WindowGroupService(
            new InMemoryWindowGroupRepository(),
            new NullWindowHost(),
            Options.Create(new WindowLayoutOptions()),
            NullLogger<WindowGroupService>.Instance);
        var windowGroups = new WindowGroupBarViewModel(
            groupService,
            NullLogger<WindowGroupBarViewModel>.Instance);
        var accounts = new UserAccountEditorViewModel(
            new InMemoryAccountRepository(),
            dataOptions,
            NullLogger<UserAccountEditorViewModel>.Instance);
        theme ??= new ThemeService();
        return new SettingsViewModel(
            repo, options, windowGroups, accounts, theme,
            NullLogger<SettingsViewModel>.Instance);
    }

    // ── 主题切换（外观段）─────────────────────────────────────────────

    [Fact]
    public void SelectedTheme_initializes_from_theme_service_current()
    {
        var theme = new ThemeService();
        theme.Apply(ApplicationTheme.Dark);
        var vm = CreateVm(theme: theme);

        vm.SelectedTheme.Should().Be(ApplicationTheme.Dark);
    }

    [Fact]
    public void Setting_SelectedTheme_applies_via_theme_service()
    {
        var theme = new ThemeService();
        theme.Apply(ApplicationTheme.Dark);
        var vm = CreateVm(theme: theme);

        vm.SelectedTheme = ApplicationTheme.Light;

        theme.Current.Should().Be(ApplicationTheme.Light, "Setting SelectedTheme 应即时应用主题");
    }

    [Fact]
    public void IsDarkTheme_true_sets_theme_to_dark()
    {
        var theme = new ThemeService();
        theme.Apply(ApplicationTheme.Light);
        var vm = CreateVm(theme: theme);

        vm.IsDarkTheme = true;

        theme.Current.Should().Be(ApplicationTheme.Dark);
        vm.SelectedTheme.Should().Be(ApplicationTheme.Dark);
    }

    [Fact]
    public void IsLightTheme_true_sets_theme_to_light()
    {
        var theme = new ThemeService();
        theme.Apply(ApplicationTheme.Dark);
        var vm = CreateVm(theme: theme);

        vm.IsLightTheme = true;

        theme.Current.Should().Be(ApplicationTheme.Light);
        vm.SelectedTheme.Should().Be(ApplicationTheme.Light);
    }

    [Fact]
    public void IsDarkTheme_and_IsLightTheme_are_mutually_exclusive()
    {
        var vm = CreateVm();

        vm.IsDarkTheme = true;
        vm.IsDarkTheme.Should().BeTrue();
        vm.IsLightTheme.Should().BeFalse();

        vm.IsLightTheme = true;
        vm.IsLightTheme.Should().BeTrue();
        vm.IsDarkTheme.Should().BeFalse();
    }

    // ── 段落切换 ─────────────────────────────────────────────────────

    [Fact]
    public void Default_section_is_window()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex.Should().Be(0);
        vm.CurrentSegment.Should().BeSameAs(vm.Window);
    }

    [Fact]
    public void Switching_to_order_section_sets_current_segment()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex = 1;

        vm.CurrentSegment.Should().BeSameAs(vm.Order);
    }

    [Fact]
    public void Switching_to_user_section_sets_current_segment()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex = 2;

        vm.CurrentSegment.Should().BeSameAs(vm.User);
    }

    [Fact]
    public void Switching_to_window_groups_section_sets_current_segment()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex = 3;

        vm.CurrentSegment.Should().BeSameAs(vm.WindowGroups);
    }

    [Fact]
    public void Switching_to_accounts_section_sets_current_segment()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex = 4;

        vm.CurrentSegment.Should().BeSameAs(vm.Accounts, "交易账号段绑定到 UserAccountEditorViewModel");
    }

    [Fact]
    public void Switching_to_appearance_section_sets_current_segment_to_self()
    {
        var vm = CreateVm();

        vm.CurrentSectionIndex = 5;

        vm.CurrentSegment.Should().BeSameAs(vm, "外观段绑定到 SettingsViewModel 自身（主题属性）");
    }

    // ── Load/Save 编排 ──────────────────────────────────────────────

    [Fact]
    public async Task Load_populates_segments_from_repository()
    {
        var vm = CreateVm(seedConfig: new CloudConfig
        {
            Window = new WindowConfig { MainFont = "TestFont", CompactSpacing = 5 },
            Order = new OrderConfig { RiskOpen = false, MaxInputCount = 20 },
            User = new UserConfig { HqAddress = "tcp://test:9999" }
        });

        await vm.LoadAsync();

        vm.State.Should().BeOfType<ConfigEditorState.Loaded>();
        vm.Window.MainFont.Should().Be("TestFont");
        vm.Window.CompactSpacing.Should().Be(5);
        vm.Order.RiskOpen.Should().BeFalse();
        vm.Order.MaxInputCount.Should().Be(20);
        vm.User.HqAddress.Should().Be("tcp://test:9999");
    }

    [Fact]
    public async Task Save_persists_modified_segments()
    {
        var repo = new InMemoryConfigRepository(SeedConfig);
        var options = Options.Create(new ConfigFileOptions { Path = "test.ini" });
        var dataOptions = Options.Create(new DataFileOptions { UsersXml = "test_users.xml" });
        var groupService = new WindowGroupService(
            new InMemoryWindowGroupRepository(),
            new NullWindowHost(),
            Options.Create(new WindowLayoutOptions()),
            NullLogger<WindowGroupService>.Instance);
        var windowGroups = new WindowGroupBarViewModel(
            groupService,
            NullLogger<WindowGroupBarViewModel>.Instance);
        var accounts = new UserAccountEditorViewModel(
            new InMemoryAccountRepository(),
            dataOptions,
            NullLogger<UserAccountEditorViewModel>.Instance);
        var vm = new SettingsViewModel(
            repo, options, windowGroups, accounts, new ThemeService(),
            NullLogger<SettingsViewModel>.Instance);

        await vm.LoadAsync();
        vm.Window.MainFont = "ModifiedFont";
        vm.Order.MaxInputCount = 99;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.LastSavedAt.Should().NotBeNull();
        var reloaded = repo.Load("test.ini");
        reloaded.Window.MainFont.Should().Be("ModifiedFont");
        reloaded.Order.MaxInputCount.Should().Be(99);
    }

    [Fact]
    public async Task Save_disabled_while_loading()
    {
        var vm = CreateVm();

        // 等待构造期间启动的自动加载结束，避免后台加载在断言前覆盖下面设定的 Loading 状态。
        await vm.LoadAsync();

        // 用反射强制设回 Loading，以验证 CanExecute 守门。
        var prop = typeof(SettingsViewModel).GetProperty(nameof(SettingsViewModel.State))!;
        prop.SetValue(vm, new ConfigEditorState.Loading());

        vm.SaveCommand.CanExecute(null).Should().BeFalse("Loading 状态 Save 不可执行");
    }

    [Fact]
    public async Task LoadAsync_during_startup_load_waits_until_save_is_enabled()
    {
        var vm = CreateVm();

        await vm.LoadAsync();

        vm.State.Should().BeOfType<ConfigEditorState.Loaded>("LoadAsync 必须等待构造时启动的自动加载完成");
        vm.SaveCommand.CanExecute(null).Should().BeTrue("加载后应可保存");
    }
}

/// <summary>
/// 内存配置仓库：用于测试 SettingsViewModel 的 Load/Save 编排，不读写真实文件。
/// </summary>
internal sealed class InMemoryConfigRepository : IConfigRepository
{
    private CloudConfig _config;

    public InMemoryConfigRepository(CloudConfig initial) => _config = initial;

    public CloudConfig Load(string path) => _config;

    public void Save(string path, CloudConfig config) => _config = config;
}

/// <summary>
/// 内存窗口分组仓库：用于测试，不读写真实 Users.xml。匹配 IWindowGroupRepository 签名。
/// </summary>
internal sealed class InMemoryWindowGroupRepository : IWindowGroupRepository
{
    private WindowLayout _layout = new();

    public WindowLayout Load(WindowLayoutOptions options) => _layout;

    public void Save(WindowLayoutOptions options, WindowLayout layout) => _layout = layout;
}

/// <summary>
/// 空窗口宿主桩：所有方法 no-op，仅满足 WindowGroupService 构造依赖。
/// </summary>
internal sealed class NullWindowHost : IWindowHost
{
    public bool IsOpen(string instrumentCode) => false;
    public void Open(InstrumentWindow window) { }
    public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId) { }
    public IReadOnlyList<string> GetOpenWindowsInGroup(int groupId) => Array.Empty<string>();
    public void Focus(string instrumentCode) { }
    public void Close(string instrumentCode) { }
    public void CloseGroup(int groupId) { }
}

/// <summary>
/// 内存账号仓库：用于测试 UserAccountEditorViewModel 的 CRUD，不读写 Users.xml。
/// </summary>
internal sealed class InMemoryAccountRepository : IAccountRepository
{
    private readonly Dictionary<string, AccountEntry> _entries = new(StringComparer.Ordinal);

    public IReadOnlyList<AccountEntry> Load(string usersXmlPath) =>
        _entries.Values.OrderBy(e => e.UserId, StringComparer.Ordinal).ToList();

    public void Add(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));
        if (_entries.ContainsKey(account.UserId))
            throw new InvalidOperationException($"UserId 已存在：{account.UserId}");
        _entries[account.UserId] = account;
    }

    public void Update(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));
        if (!_entries.ContainsKey(account.UserId))
            throw new InvalidOperationException($"未找到账号 {account.UserId}");
        _entries[account.UserId] = account;
    }

    public void Delete(string usersXmlPath, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId 不能为空", nameof(userId));
        _entries.Remove(userId);
    }
}
