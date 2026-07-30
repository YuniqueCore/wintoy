using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 登录页 ViewModel：管理账号列表、行情地址列表、TCP 测速、登录流程。
/// <para>
/// 数据来源：HQAddress.xml（行情上游）+ Users.xml（多账号）。
/// 登录成功后触发 <see cref="LoginSucceeded"/> 事件，由 Host 切换到 FloatingMainWindow。
/// </para>
/// </summary>
public partial class LoginViewModel : ObservableObject
{
    private readonly IHqAddressRepository _hqRepo;
    private readonly IAccountRepository _accountRepo;
    private readonly IConnectionProbeService _probeService;
    private readonly ISessionService _sessionService;
    private readonly LoginOptions _loginOptions;
    private readonly ILogger<LoginViewModel> _logger;

    /// <summary>行情上游地址列表（含测速延迟）。</summary>
    [ObservableProperty] private ObservableCollection<HqAddressEntry> _marketAddresses = [];

    /// <summary>当前选中的行情地址；登录时作为行情连接端点。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private HqAddressEntry? _selectedMarketAddress;

    /// <summary>交易账号列表（从 Users.xml 加载）。</summary>
    [ObservableProperty] private ObservableCollection<AccountEntry> _accounts = [];

    /// <summary>当前选中的账号；选中后自动展示交易地址并测速。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private AccountEntry? _selectedAccount;

    /// <summary>登录密码。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    /// <summary>登录后是否刷新合约列表（ReqQryInstrument）。</summary>
    [ObservableProperty] private bool _refreshContracts = true;

    /// <summary>独立模式开关（默认勾选，对齐 0527.exe 登录页）。</summary>
    [ObservableProperty] private bool _independent = true;

    /// <summary>备用模式开关。</summary>
    [ObservableProperty] private bool _backup;

    /// <summary>登录页 UI 状态机。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private LoginState _state = new LoginState.Idle();

    /// <summary>状态栏消息（供 InfoBar 展示）。</summary>
    [ObservableProperty] private string _statusMessage = "就绪";

    /// <summary>当前选中账号的交易地址延迟（ms）；null 表示未测速。</summary>
    [ObservableProperty] private double? _tradingAddressLatency;

    /// <summary>登录成功事件（Host 订阅后切换到 FloatingMainWindow）。</summary>
    public event EventHandler? LoginSucceeded;

    /// <summary>请求打开设置窗口事件。</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>登录命令是否可执行：账号/行情已选 + 密码非空 + 状态为 Idle/Failed。</summary>
    public bool CanLogin =>
        SelectedAccount is not null
        && SelectedMarketAddress is not null
        && !string.IsNullOrEmpty(Password)
        && State is LoginState.Idle or LoginState.Failed;

    public LoginViewModel(
        IHqAddressRepository hqRepo,
        IAccountRepository accountRepo,
        IConnectionProbeService probeService,
        ISessionService sessionService,
        IOptions<LoginOptions> loginOptions,
        ILogger<LoginViewModel> logger)
    {
        _hqRepo = hqRepo;
        _accountRepo = accountRepo;
        _probeService = probeService;
        _sessionService = sessionService;
        _loginOptions = loginOptions.Value;
        _logger = logger;

        // 构造后立即加载数据 + 自动测速（fire-and-forget，内部异常捕获）
        _ = LoadDataAsync();
    }

    /// <summary>加载账号列表 + 行情地址列表，并自动测速。</summary>
    private async Task LoadDataAsync()
    {
        try
        {
            StatusMessage = "正在加载配置...";

            // 加载行情地址
            var hqAddresses = _hqRepo.Load(_loginOptions.HqAddressXmlPath);
            MarketAddresses = new ObservableCollection<HqAddressEntry>(hqAddresses);
            if (MarketAddresses.Count > 0)
                SelectedMarketAddress = MarketAddresses[0];

            // 加载账号
            var accounts = _accountRepo.Load(_loginOptions.UsersXmlPath);
            // Mock 模式下若 Users.xml 无账号，注入测试账号（来自 appsettings）
            if (accounts.Count == 0 && _loginOptions.UseMock)
            {
                accounts = [new AccountEntry
                {
                    Title = "000102",
                    UserId = "000102",
                    BrokerId = "8080",
                    TradingAddress = "tcp://60.12.233.58:18105",
                    AppId = "client_qihuo159_1.0",
                    AuthCode = "AC2F6ESEXEEYSIGU"
                }];
            }
            Accounts = new ObservableCollection<AccountEntry>(accounts);
            if (Accounts.Count > 0)
                SelectedAccount = Accounts[0];

            StatusMessage = $"已加载 {MarketAddresses.Count} 个行情地址，{Accounts.Count} 个账号";
            _logger.LogInformation("登录页数据加载完成：{MdCount} 行情地址，{AcctCount} 账号",
                MarketAddresses.Count, Accounts.Count);

            // 自动测速
            await ProbeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载数据失败");
            StatusMessage = $"加载失败：{ex.Message}";
        }
    }

    /// <summary>测速命令：并发探测全部行情地址 + 当前账号交易地址的 TCP 延迟。</summary>
    [RelayCommand]
    private async Task ProbeAsync()
    {
        if (MarketAddresses.Count == 0) return;

        State = new LoginState.Probing();
        StatusMessage = "正在测速...";

        var timeout = TimeSpan.FromMilliseconds(_loginOptions.ProbeTimeoutMs);
        var endpoints = MarketAddresses
            .Select(a => (a.Host, a.Port))
            .ToList();

        await _probeService.ProbeAllAsync(
            endpoints, timeout,
            result => System.Windows.Application.Current.Dispatcher.InvokeAsync(() => UpdateMarketAddressLatency(result)));

        // 测速完成后回到 Idle（即使部分失败也算测速完成）
        State = new LoginState.Idle();
        var okCount = MarketAddresses.Count(a => a.ProbeSuccess);
        StatusMessage = $"测速完成：{okCount}/{MarketAddresses.Count} 可达";

        // 同时测当前账号交易地址
        await ProbeTradingAddressAsync();
    }

    /// <summary>登录命令：调用 SessionService 连接行情 + 交易。</summary>
    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        if (SelectedAccount is null || SelectedMarketAddress is null) return;

        State = new LoginState.LoggingIn();
        StatusMessage = "正在登录...";

        try
        {
            var request = new LoginRequest(
                SelectedAccount,
                SelectedMarketAddress,
                Password,
                RefreshContracts);

            var result = await _sessionService.LoginAsync(request);

            if (result is SessionState.LoggedIn)
            {
                StatusMessage = "登录成功";
                _logger.LogInformation("登录成功，触发窗口切换");
                LoginSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else if (result is SessionState.Failed failed)
            {
                State = new LoginState.Failed(failed.Error);
                StatusMessage = $"登录失败：{failed.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录异常");
            State = new LoginState.Failed(ex.Message);
            StatusMessage = $"登录异常：{ex.Message}";
        }
    }

    /// <summary>打开设置窗口。</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>选中账号变更 → 更新交易地址延迟。</summary>
    partial void OnSelectedAccountChanged(AccountEntry? value)
    {
        TradingAddressLatency = null;
        if (value is not null)
            _ = ProbeTradingAddressAsync();
    }

    /// <summary>测速当前账号的交易地址。</summary>
    private async Task ProbeTradingAddressAsync()
    {
        if (SelectedAccount is null) return;
        var (host, port) = ParseAddress(SelectedAccount.TradingAddress);
        if (host is null) return;

        var timeout = TimeSpan.FromMilliseconds(_loginOptions.ProbeTimeoutMs);
        var result = await _probeService.ProbeAsync(host, port, timeout);
        TradingAddressLatency = result.Success ? result.RttMs : null;
    }

    /// <summary>更新行情地址列表中匹配条目的延迟。</summary>
    private void UpdateMarketAddressLatency(ProbeResult result)
    {
        for (var i = 0; i < MarketAddresses.Count; i++)
        {
            var addr = MarketAddresses[i];
            if (addr.Host == result.Host && addr.Port == result.Port)
            {
                MarketAddresses[i] = addr with
                {
                    LatencyMs = result.RttMs,
                    ProbeSuccess = result.Success
                };
                break;
            }
        }
    }

    /// <summary>从 tcp://host:port 解析出 host 和 port。</summary>
    private static (string? Host, int Port) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return (null, 0);
        try
        {
            var uri = new Uri(address);
            return (uri.Host, uri.Port);
        }
        catch
        {
            return (null, 0);
        }
    }
}
