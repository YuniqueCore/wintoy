using FluentAssertions;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="UserAccountEditorViewModel"/> 单元测试：覆盖 Load / Add / Update / Delete 的 UI 编排路径，
/// 状态机迁移（Idle → Loading → Loaded / Error），CanExecute 切换，SelectedAccount 同步。
/// 用内存 <see cref="FakeAccountRepository"/> 隔离 IAccountRepository，不读写 Users.xml。
/// </summary>
public class UserAccountEditorViewModelTests
{
    private static IOptions<DataFileOptions> Opts() =>
        Options.Create(new DataFileOptions { UsersXml = "test_users.xml" });

    private static UserAccountEditorViewModel CreateVm(out FakeAccountRepository repo) =>
        new(repo = new FakeAccountRepository(), Opts(), NullLogger<UserAccountEditorViewModel>.Instance);

    /// <summary>预先 Seed 仓库再创建 VM，避免 auto-load 在 Seed 之前完成导致读到空。</summary>
    private static UserAccountEditorViewModel CreateVmSeeded(out FakeAccountRepository repo, params AccountEntry[] accounts)
    {
        repo = new FakeAccountRepository();
        repo.Seed(accounts);
        return new UserAccountEditorViewModel(repo, Opts(), NullLogger<UserAccountEditorViewModel>.Instance);
    }

    // ── Auto-load (构造即自动加载) ──────────────────────────

    [Fact]
    public async Task Constructor_auto_loads_accounts_from_repository()
    {
        var vm = CreateVmSeeded(out var repo, Sample("111111"));

        // 构造已自动触发 LoadAsync，等其完成
        await WaitForState(vm, s => s is UserAccountEditorState.Loaded);

        vm.Accounts.Should().ContainSingle().Which.UserId.Should().Be("111111");
        repo.LoadCount.Should().Be(1, "构造期间仅触发一次加载");
    }

    [Fact]
    public async Task EnsureLoaded_is_noop_when_already_loaded()
    {
        var vm = CreateVmSeeded(out var repo, Sample("111111"));
        await WaitForState(vm, s => s is UserAccountEditorState.Loaded);

        var initialCount = repo.LoadCount;
        vm.EnsureLoaded();
        // 给个微小的等待时间确保不会重新加载
        await Task.Delay(50);

        repo.LoadCount.Should().Be(initialCount, "Loaded 状态时 EnsureLoaded 不应触发重新加载");
    }

    [Fact]
    public async Task EnsureLoaded_retries_after_error()
    {
        var vm = CreateVmSeeded(out var repo, Sample("111111"));
        await WaitForState(vm, s => s is UserAccountEditorState.Loaded);

        // 模拟错误状态后 EnsureLoaded 应重试
        SetState(vm, new UserAccountEditorState.Error("test"));
        vm.EnsureLoaded();
        await WaitForState(vm, s => s is UserAccountEditorState.Loaded);

        repo.LoadCount.Should().BeGreaterThanOrEqualTo(2, "Error 状态时 EnsureLoaded 应触发重试");
    }

    // ── Add ─────────────────────────────────────────────────

    [Fact]
    public async Task Add_appends_account_and_resets_form_on_success()
    {
        var vm = CreateVm(out _);
        await LoadedAsync(vm);

        vm.NewUserId = "999999";
        vm.NewBrokerId = "99999";
        vm.NewAuthCode = "NEWAUTH";

        vm.AddCommand.Execute(null);

        vm.Accounts.Should().ContainSingle().Which.UserId.Should().Be("999999");
        vm.NewUserId.Should().BeEmpty("新增成功后表单应清空");
        vm.LastSavedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Add_with_empty_userid_sets_error_state()
    {
        var vm = CreateVm(out _);
        await LoadedAsync(vm);

        vm.NewUserId = "  ";
        vm.AddCommand.Execute(null);

        vm.State.Should().BeOfType<UserAccountEditorState.Error>();
        ((UserAccountEditorState.Error)vm.State).Message.Should().Contain("UserId");
    }

    [Fact]
    public async Task Add_duplicate_userid_sets_error_state()
    {
        var vm = CreateVm(out var repo);
        repo.Seed(Sample("111111"));
        await LoadedAsync(vm);

        vm.NewUserId = "111111";
        vm.AddCommand.Execute(null);

        vm.State.Should().BeOfType<UserAccountEditorState.Error>();
        ((UserAccountEditorState.Error)vm.State).Message.Should().Contain("111111");
    }

    // ── Update ──────────────────────────────────────────────

    [Fact]
    public async Task Update_modifies_selected_account_persists()
    {
        var vm = CreateVm(out var repo);
        repo.Seed(Sample("111111", brokerId: "88888", title: "Old"));
        await LoadedAsync(vm);
        vm.SelectedAccount = vm.Accounts[0];

        // 模拟用户修改后回写到 SelectedAccount；为简化，直接替换列表项
        var updated = Sample("111111", brokerId: "99999", title: "New");
        vm.Accounts[0] = updated;
        vm.SelectedAccount = updated;

        vm.UpdateCommand.Execute(null);

        vm.State.Should().BeOfType<UserAccountEditorState.Loaded>();
        repo.UpdatedEntries.Should().ContainSingle()
            .Which.BrokerId.Should().Be("99999");
        vm.LastSavedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_disabled_when_no_selection()
    {
        var vm = CreateVm(out _);
        await LoadedAsync(vm);
        vm.SelectedAccount = null;

        vm.UpdateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Update_disabled_while_loading()
    {
        var vm = CreateVm(out _);
        SetState(vm, new UserAccountEditorState.Loading());

        vm.UpdateCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Delete ──────────────────────────────────────────────

    [Fact]
    public async Task Delete_removes_selected_account_and_clears_selection()
    {
        var vm = CreateVm(out var repo);
        repo.Seed(Sample("111111"), Sample("222222"));
        await LoadedAsync(vm);
        vm.SelectedAccount = vm.Accounts.First(a => a.UserId == "111111");

        vm.DeleteCommand.Execute(null);

        vm.Accounts.Should().ContainSingle().Which.UserId.Should().Be("222222");
        vm.SelectedAccount.Should().BeNull();
        repo.DeletedUserIds.Should().Contain("111111");
        vm.LastSavedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_disabled_when_no_selection()
    {
        var vm = CreateVm(out _);
        await LoadedAsync(vm);
        vm.SelectedAccount = null;

        vm.DeleteCommand.CanExecute(null).Should().BeFalse();
    }

    // ── State machine ───────────────────────────────────────

    [Fact]
    public void Initial_state_is_Loading_until_auto_load_completes()
    {
        var vm = CreateVm(out _);

        // 构造即触发自动加载，期间 State 为 Loading
        vm.State.Should().BeOfType<UserAccountEditorState.Loading>();
    }

    [Fact]
    public async Task Auto_load_transitions_to_Loaded_on_success()
    {
        var vm = CreateVm(out var repo);
        repo.Seed(Sample("111111"), Sample("222222"));

        await WaitForState(vm, s => s is UserAccountEditorState.Loaded);

        vm.Accounts.Should().HaveCount(2);
        vm.State.Should().BeOfType<UserAccountEditorState.Loaded>();
    }

    // ── EnsureLoaded is no-op while loading ──────────────────

    [Fact]
    public void EnsureLoaded_is_noop_while_loading()
    {
        var vm = CreateVm(out var repo);
        repo.Seed(Sample("111111"));
        SetState(vm, new UserAccountEditorState.Loading());

        vm.EnsureLoaded();
        // 状态保持 Loading，因为 EnsureLoaded 只在 (Loading|Error) 且 Accounts.Count==0 时重试
        vm.State.Should().BeOfType<UserAccountEditorState.Loading>("Loading 中有数据时 EnsureLoaded 不应触发再次加载");
    }

    // ── helpers ─────────────────────────────────────────────

    private static AccountEntry Sample(string userId, string brokerId = "88888", string title = "Test") => new()
    {
        Title = title,
        TradingAddress = "tcp://127.0.0.1:42205",
        BrokerId = brokerId,
        UserId = userId,
        AppId = "Weg_yiyisy_V1.0",
        AuthCode = "AUTHCODE123",
    };

    private static async Task LoadedAsync(UserAccountEditorViewModel vm)
    {
        vm.EnsureLoaded();
        await WaitForState(vm, s => s is UserAccountEditorState.Loaded or UserAccountEditorState.Error);
    }

    private static async Task WaitForState(UserAccountEditorViewModel vm, Func<UserAccountEditorState, bool> predicate)
    {
        for (var i = 0; i < 100; i++)
        {
            if (predicate(vm.State)) return;
            await Task.Delay(20);
        }
    }

    /// <summary>通过反射设置 State（State 是 private set，仅测试用）。</summary>
    private static void SetState(UserAccountEditorViewModel vm, UserAccountEditorState state)
    {
        var prop = typeof(UserAccountEditorViewModel).GetProperty(nameof(UserAccountEditorViewModel.State))!;
        prop.SetValue(vm, state);
    }
}

/// <summary>
/// 内存账号仓库：用于测试 UserAccountEditorViewModel 的 UI 编排。
/// 记录 Load/Add/Update/Delete 调用次数和参数，便于断言副作用。
/// </summary>
internal sealed class FakeAccountRepository : IAccountRepository
{
    private readonly Dictionary<string, AccountEntry> _entries = new(StringComparer.Ordinal);

    public int LoadCount { get; private set; }
    public List<AccountEntry> AddedEntries { get; } = new();
    public List<AccountEntry> UpdatedEntries { get; } = new();
    public List<string> DeletedUserIds { get; } = new();

    public void Seed(params AccountEntry[] accounts)
    {
        foreach (var a in accounts) _entries[a.UserId] = a;
    }

    public IReadOnlyList<AccountEntry> Load(string usersXmlPath)
    {
        LoadCount++;
        return _entries.Values.OrderBy(e => e.UserId, StringComparer.Ordinal).ToList();
    }

    public void Add(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));
        if (_entries.ContainsKey(account.UserId))
            throw new InvalidOperationException($"UserId 已存在：{account.UserId}");
        _entries[account.UserId] = account;
        AddedEntries.Add(account);
    }

    public void Update(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));
        if (!_entries.ContainsKey(account.UserId))
            throw new InvalidOperationException($"未找到账号 {account.UserId}");
        _entries[account.UserId] = account;
        UpdatedEntries.Add(account);
    }

    public void Delete(string usersXmlPath, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId 不能为空", nameof(userId));
        _entries.Remove(userId);
        DeletedUserIds.Add(userId);
    }
}
