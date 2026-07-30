using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 交易账号段（Settings 第 5 段）的可编辑视图状态：Users.xml 顶层 &lt;User&gt; 元素的 CRUD 视图。
/// <para>
/// 启动时自动从 Users.xml 加载（无需用户点加载按钮），所有变更（新增/修改/删除）立即落盘
/// （按 0527 习惯：每个动作独立落盘，配置变更即时可见）。失败时 <c>State</c> 切到 Error 反馈。
/// </para>
/// </summary>
public sealed partial class UserAccountEditorViewModel : ObservableObject
{
    private readonly IAccountRepository _repo;
    private readonly DataFileOptions _options;
    private readonly ILogger<UserAccountEditorViewModel> _logger;

    public UserAccountEditorViewModel(
        IAccountRepository repo,
        IOptions<DataFileOptions> options,
        ILogger<UserAccountEditorViewModel> logger)
    {
        _repo = repo;
        _options = options.Value;
        _logger = logger;
        // 启动即自动加载最新 Users.xml，无需用户手动点加载按钮
        _ = LoadAsync();
    }

    /// <summary>所有交易账号（从 Users.xml 加载，按文件中顺序展示）。</summary>
    public ObservableCollection<AccountEntry> Accounts { get; } = [];

    /// <summary>当前选中的账号（表单编辑/删除目标）。</summary>
    [ObservableProperty]
    public partial AccountEntry? SelectedAccount { get; set; }

    /// <summary>新建账号的表单输入：6 个连接字段。</summary>
    [ObservableProperty]
    public partial string NewTitle { get; set; } = "";

    [ObservableProperty]
    public partial string NewTradingAddress { get; set; } = "tcp://122.224.130.77:42205";

    [ObservableProperty]
    public partial string NewBrokerId { get; set; } = "88888";

    [ObservableProperty]
    public partial string NewUserId { get; set; } = "";

    [ObservableProperty]
    public partial string NewAppId { get; set; } = "Weg_yiyisy_V1.0";

    [ObservableProperty]
    public partial string NewAuthCode { get; set; } = "";

    /// <summary>编辑模式下保存到磁盘时间。</summary>
    [ObservableProperty]
    public partial DateTime? LastSavedAt { get; set; }

    /// <summary>当前状态：Idle/Loaded/Error 三态。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial UserAccountEditorState State { get; private set; } = new UserAccountEditorState.Loading();

    /// <summary>错误后重试加载（构造已自动加载；Loading 状态不应重复触发）。</summary>
    public void EnsureLoaded()
    {
        if (State is UserAccountEditorState.Error)
            _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        State = new UserAccountEditorState.Loading();
        try
        {
            var list = await Task.Run(() => _repo.Load(_options.UsersXml));
            Accounts.Clear();
            foreach (var a in list) Accounts.Add(a);
            State = new UserAccountEditorState.Loaded();
            _logger.LogInformation("已加载 {Count} 个交易账号（{Path}）", list.Count, _options.UsersXml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载账号失败");
            State = new UserAccountEditorState.Error(ex.Message);
        }
    }

    /// <summary>新增账号（按表单字段）。UserId 必填且唯一。</summary>
    [RelayCommand(CanExecute = nameof(CanModify))]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewUserId))
        {
            State = new UserAccountEditorState.Error("UserId 不能为空");
            return;
        }
        try
        {
            var entry = new AccountEntry
            {
                Title = NewTitle,
                TradingAddress = NewTradingAddress,
                BrokerId = NewBrokerId,
                UserId = NewUserId.Trim(),
                AppId = NewAppId,
                AuthCode = NewAuthCode,
            };
            _repo.Add(_options.UsersXml, entry);
            Accounts.Add(entry);
            ClearNewEntryForm();
            LastSavedAt = DateTime.Now;
            State = new UserAccountEditorState.Loaded();
            _logger.LogInformation("新增账号 {UserId}", entry.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "新增账号失败");
            State = new UserAccountEditorState.Error(ex.Message);
        }
    }

    /// <summary>更新当前选中账号的连接信息（按表单字段）。</summary>
    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Update()
    {
        if (SelectedAccount is null) return;
        try
        {
            var updated = new AccountEntry
            {
                Title = SelectedAccount.Title,
                TradingAddress = SelectedAccount.TradingAddress,
                BrokerId = SelectedAccount.BrokerId,
                UserId = SelectedAccount.UserId,
                AppId = SelectedAccount.AppId,
                AuthCode = SelectedAccount.AuthCode,
            };
            _repo.Update(_options.UsersXml, updated);

            // 同步列表中的对象
            var idx = Accounts.IndexOf(SelectedAccount);
            if (idx >= 0) Accounts[idx] = updated;
            SelectedAccount = updated;
            LastSavedAt = DateTime.Now;
            State = new UserAccountEditorState.Loaded();
            _logger.LogInformation("更新账号 {UserId}", updated.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新账号失败");
            State = new UserAccountEditorState.Error(ex.Message);
        }
    }

    /// <summary>删除当前选中账号（含 WindowHistory）。</summary>
    [RelayCommand(CanExecute = nameof(CanModifySelected))]
    private void Delete()
    {
        if (SelectedAccount is null) return;
        try
        {
            _repo.Delete(_options.UsersXml, SelectedAccount.UserId);
            Accounts.Remove(SelectedAccount);
            SelectedAccount = null;
            LastSavedAt = DateTime.Now;
            State = new UserAccountEditorState.Loaded();
            _logger.LogInformation("删除账号 {UserId}", SelectedAccount?.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除账号失败");
            State = new UserAccountEditorState.Error(ex.Message);
        }
    }

    private void ClearNewEntryForm()
    {
        NewTitle = "";
        NewUserId = "";
        NewAuthCode = "";
        NewBrokerId = "88888";
        NewTradingAddress = "tcp://122.224.130.77:42205";
        NewAppId = "Weg_yiyisy_V1.0";
    }

    private bool CanModify() => State is UserAccountEditorState.Loaded;
    private bool CanModifySelected() => State is UserAccountEditorState.Loaded && SelectedAccount is not null;
}

/// <summary>账号编辑器状态机：Idle / Loading / Loaded / Error（discriminated union，避免零散 isLoading）。</summary>
public abstract record UserAccountEditorState
{
    public sealed record Idle : UserAccountEditorState;
    public sealed record Loading : UserAccountEditorState;
    public sealed record Loaded : UserAccountEditorState;
    public sealed record Error(string Message) : UserAccountEditorState;
}
