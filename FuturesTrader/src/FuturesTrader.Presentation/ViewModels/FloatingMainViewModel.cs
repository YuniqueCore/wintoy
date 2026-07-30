using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.WindowHosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 浮动工具栏 ViewModel（桌面底部长条）：账号 ID + 20 分组按钮 + 模式开关 + 资金摘要。
/// <para>
/// 登录成功后由 Host 显示。分组按钮点击 → <see cref="WindowGroupService.OpenGroup"/> 打开该组全部合约窗口。
/// 资金摘要由 <see cref="AccountSummaryViewModel"/> 订阅 <see cref="ITradingService"/> 流聚合。
/// </para>
/// <para>
/// 模式开关（单/多/全部·仓/平·A/B·标尺/白格/两排）与点价窗口双向联动（M4-C ContractWindowViewModel 订阅）。
/// <see cref="AlwaysOnTop"/> 控制窗口 Topmost（对齐 0527.exe「置顶可取消，配置按钮控制」）。
/// </para>
/// </summary>
public sealed partial class FloatingMainViewModel : ObservableObject, IDisposable
{
    private readonly ISessionService _session;
    private readonly WindowGroupService _groupService;
    private readonly IWindowHost _windowHost;
    private readonly GroupSynchronizationCoordinator _sync;
    private readonly UiOptions _uiOptions;
    private readonly ILogger<FloatingMainViewModel> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly CompositeDisposable _subscriptions = new();
    private WindowLayout? _layout;
    private readonly ObservableCollection<Instrument> _allInstruments = new();
    private bool _disposed;

    public FloatingMainViewModel(
        ISessionService session,
        WindowGroupService groupService,
        IWindowHost windowHost,
        GroupSynchronizationCoordinator sync,
        IOptions<UiOptions> uiOptions,
        ILogger<FloatingMainViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        _session = session;
        _groupService = groupService;
        _windowHost = windowHost;
        _sync = sync;
        _uiOptions = uiOptions.Value;
        _logger = logger;
        _loggerFactory = loggerFactory;

        Groups = new ObservableCollection<GroupButtonViewModel>(
            Enumerable.Range(1, 20).Select(i => new GroupButtonViewModel
            {
                Id = i,
                Name = $"组 {i}",
                Parent = this
            }));
        // 拆分为两行（1-10 / 11-20）供浮动栏 2×10 网格布局
        GroupsRow1 = new ObservableCollection<GroupButtonViewModel>(Groups.Take(10));
        GroupsRow2 = new ObservableCollection<GroupButtonViewModel>(Groups.Skip(10));

        AlwaysOnTop = _uiOptions.AlwaysOnTop;

        // 从会话取账号 ID
        AccountId = _session.Account?.UserId ?? "(未登录)";

        // 资金摘要：从会话取交易服务（登录后非空）
        if (_session.Trading is not null)
        {
            AccountSummary = new AccountSummaryViewModel(
                _session.Trading,
                _loggerFactory.CreateLogger<AccountSummaryViewModel>());
            // 订阅合约流，累积全量合约供搜索 autocomplete（CTP 全量查询逐条推送）
            var instSub = _session.Trading.InstrumentStream.Subscribe(OnInstrumentReceived);
            _subscriptions.Add(instSub);
        }
    }

    /// <summary>当前账号 ID（登录后从 SessionService.Account 取）。</summary>
    [ObservableProperty] private string _accountId = "(未登录)";

    /// <summary>20 个分组按钮（1-20 号，2×10 网格布局）。</summary>
    public ObservableCollection<GroupButtonViewModel> Groups { get; }

    /// <summary>第一行分组按钮（1-10 号）。</summary>
    public ObservableCollection<GroupButtonViewModel> GroupsRow1 { get; }

    /// <summary>第二行分组按钮（11-20 号）。</summary>
    public ObservableCollection<GroupButtonViewModel> GroupsRow2 { get; }

    /// <summary>显示范围模式：单/多/全部。</summary>
    [ObservableProperty] private FloatingDisplayMode _displayMode = FloatingDisplayMode.Single;

    /// <summary>开平仓模式：仓/平（与点价窗口 OnlyOpen 联动）。</summary>
    [ObservableProperty] private FloatingOrderMode _orderMode = FloatingOrderMode.Open;

    /// <summary>挂单模式 A/B（与点价窗口 ChgOrder 联动）。</summary>
    [ObservableProperty] private FloatingAbMode _abMode = FloatingAbMode.A;

    /// <summary>标尺开关（显示价格标尺）。</summary>
    [ObservableProperty] private bool _showRuler = true;

    /// <summary>白格开关（与点价窗口白格单锁联动）。</summary>
    [ObservableProperty] private bool _showWhiteGrid;

    /// <summary>两排布局开关（价格梯子双排显示）。</summary>
    [ObservableProperty] private bool _twoRowLayout;

    /// <summary>窗口置顶开关（Topmost，对齐 0527.exe 配置按钮控制）。</summary>
    [ObservableProperty] private bool _alwaysOnTop;

    /// <summary>窗口同步模式（成组联动 / 完全独立）。</summary>
    [ObservableProperty] private WindowSyncMode _syncMode = WindowSyncMode.Grouped;

    /// <summary>资金持仓摘要 VM（市/净/可/持/权/手）。</summary>
    public AccountSummaryViewModel? AccountSummary { get; }

    /// <summary>合约搜索文本（auto-complete-input 输入）。</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>搜索过滤后的合约列表（autocomplete 下拉，最多 20 条）。</summary>
    public ObservableCollection<Instrument> FilteredInstruments { get; } = new();

    /// <summary>autocomplete Popup 是否打开。</summary>
    [ObservableProperty] private bool _isSearchPopupOpen;

    /// <summary>autocomplete 列表选中项（选中后触发添加到分组）。</summary>
    [ObservableProperty] private Instrument? _selectedInstrument;

    /// <summary>当前选中分组号（0=未选中，搜索添加合约时绑定到此组）。</summary>
    [ObservableProperty] private int _selectedGroupId;

    /// <summary>登出事件（Host 订阅后关闭浮动栏 + 回登录页）。</summary>
    public event EventHandler? LogoutRequested;

    /// <summary>请求打开设置窗口事件。</summary>
    public event EventHandler? OpenSettingsRequested;

    /// <summary>初始化：加载窗口分组布局并刷新分组按钮的窗口数。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            _layout = await Task.Run(() => _groupService.Load());
            RefreshGroupButtons();
            _logger.LogInformation("浮动栏已加载分组布局：{Count} 窗口", _layout.Windows.Count);
            // 触发全量合约查询（供搜索 autocomplete，CTP 逐条推送累积到 _allInstruments）
            _session.Trading?.QueryInstrumentAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载窗口分组失败");
        }
    }

    /// <summary>分组按钮点击回调：打开该组全部窗口。</summary>
    public void OpenGroupFromButton(int groupId)
    {
        try
        {
            if (_layout is null)
            {
                _layout = _groupService.Load();
            }
            _groupService.OpenGroup(_layout, groupId);
            SelectedGroupId = groupId;
            foreach (var btn in Groups) btn.IsSelected = btn.Id == groupId;
            RefreshGroupButtons();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开分组 {GroupId} 失败", groupId);
        }
    }

    /// <summary>切换置顶开关。</summary>
    [RelayCommand]
    private void ToggleTopmost()
    {
        AlwaysOnTop = !AlwaysOnTop;
    }

    /// <summary>切换窗口同步模式（成组/独立）。</summary>
    [RelayCommand]
    private void ToggleSyncMode()
    {
        SyncMode = SyncMode == WindowSyncMode.Grouped
            ? WindowSyncMode.Independent
            : WindowSyncMode.Grouped;
        _sync.SyncMode = SyncMode;
    }

    /// <summary>打开设置窗口。</summary>
    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>登出：关闭所有合约窗口 + 断开会话。</summary>
    [RelayCommand]
    private async Task LogoutAsync()
    {
        _logger.LogInformation("用户请求登出");
        // 关闭所有分组的窗口
        for (var g = 1; g <= 20; g++)
            _windowHost.CloseGroup(g);
        LogoutRequested?.Invoke(this, EventArgs.Empty);
        await _session.LogoutAsync();
    }

    /// <summary>SearchText 变更：过滤合约列表（按代码或名称模糊匹配，最多 20 条）。</summary>
    partial void OnSearchTextChanged(string value)
    {
        FilteredInstruments.Clear();
        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearchPopupOpen = false;
            return;
        }
        var matches = _allInstruments
            .Where(i => i.InstrumentId.Contains(value, StringComparison.OrdinalIgnoreCase)
                     || i.Name.Contains(value, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToArray();
        foreach (var m in matches) FilteredInstruments.Add(m);
        IsSearchPopupOpen = FilteredInstruments.Count > 0;
    }

    /// <summary>autocomplete 选中项变更：触发添加到分组命令后重置选中。</summary>
    partial void OnSelectedInstrumentChanged(Instrument? value)
    {
        if (value is not null)
        {
            AddInstrumentToGroupCommand.Execute(value);
            SelectedInstrument = null;
        }
    }

    /// <summary>合约流推送：累积到全量缓存（CTP 全量查询逐条推送）。</summary>
    private void OnInstrumentReceived(Instrument instrument)
    {
        _allInstruments.Add(instrument);
    }

    /// <summary>选中合约后添加到当前分组：AssignWindowToGroup + Save + Open 新窗口。</summary>
    [RelayCommand]
    private void AddInstrumentToGroup(Instrument? instrument)
    {
        if (instrument is null || SelectedGroupId < 1) return;
        try
        {
            if (_layout is null) _layout = _groupService.Load();
            _layout = _groupService.AssignWindowToGroup(_layout, instrument.InstrumentId, SelectedGroupId);
            _groupService.Save(_layout);
            RefreshGroupButtons();
            _windowHost.Open(new InstrumentWindow { InstrumentCode = instrument.InstrumentId, GroupId = SelectedGroupId });
            SearchText = string.Empty;
            _logger.LogInformation("已添加合约 {Instrument} 到分组 {Group}", instrument.InstrumentId, SelectedGroupId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "添加合约到分组失败");
        }
    }

    /// <summary>刷新分组按钮的窗口数 + 选中态。</summary>
    private void RefreshGroupButtons()
    {
        if (_layout is null) return;
        foreach (var btn in Groups)
        {
            btn.WindowCount = _layout.Windows.Count(w => w.GroupId == btn.Id);
            var group = _layout.Groups.FirstOrDefault(g => g.Id == btn.Id);
            if (group is not null && !string.IsNullOrWhiteSpace(group.Name))
                btn.Name = group.Name;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AccountSummary?.Dispose();
        _subscriptions.Dispose();
    }
}
