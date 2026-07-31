using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Presentation.Abstractions;
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
/// 仓/平和 A/B 全局动作由窗口宿主应用到已经创建的合约窗口；未打开窗口仍按其自身 Users.xml 配置创建。
/// <see cref="AlwaysOnTop"/> 控制窗口 Topmost（对齐 0527.exe「置顶可取消，配置按钮控制」）。
/// </para>
/// </summary>
public sealed partial class FloatingMainViewModel : ObservableObject, IDisposable
{
    private readonly ISessionService _session;
    private readonly WindowGroupService _groupService;
    private readonly IWindowHost _windowHost;
    private readonly ITradingWindowInteractionService _tradingWindowInteraction;
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
        ITradingWindowInteractionService tradingWindowInteraction,
        GroupSynchronizationCoordinator sync,
        IOptions<UiOptions> uiOptions,
        ILogger<FloatingMainViewModel> logger,
        ILoggerFactory loggerFactory)
    {
        _session = session;
        _groupService = groupService;
        _windowHost = windowHost;
        _tradingWindowInteraction = tradingWindowInteraction;
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

    /// <summary>显示范围模式选项（供 <c>SegmentedControl</c> 绑定，复刻 0527 浮动栏「单/多/全部」）。</summary>
    public IReadOnlyList<OptionItem> DisplayModeOptions { get; } = new[]
    {
        new OptionItem(FloatingDisplayMode.Single, "单", "仅显示当前选中分组"),
        new OptionItem(FloatingDisplayMode.Multi,  "多", "显示多个分组"),
        new OptionItem(FloatingDisplayMode.All,   "全部", "显示全部分组"),
    };

    /// <summary>开平仓模式选项（供 <c>SegmentedControl</c> 绑定，复刻「仓/平」二选一）。</summary>
    public IReadOnlyList<OptionItem> OrderModeOptions { get; } = new[]
    {
        new OptionItem(FloatingOrderMode.Open,  "仓", "开仓模式（OnlyOpen=true）"),
        new OptionItem(FloatingOrderMode.Close, "平", "平仓模式（OnlyOpen=false，P 标识）"),
    };

    /// <summary>挂单模式 A/B 选项（供 <c>SegmentedControl</c> 绑定，对齐 0527 Users.xml RBOA/RBOB）。</summary>
    public IReadOnlyList<OptionItem> AbModeOptions { get; } = new[]
    {
        new OptionItem(FloatingAbMode.A, "A", "单方向单点（RBOA）"),
        new OptionItem(FloatingAbMode.B, "B", "单方向多点（RBOB）"),
    };

    /// <summary>标尺开关（显示价格标尺）。</summary>
    [ObservableProperty] private bool _showRuler = true;

    /// <summary>浮动栏白格显示偏好。CBBGDS 的全局传播尚无已证实调用链，故不以该值改写合约窗下单限制。</summary>
    [ObservableProperty] private bool _showWhiteGrid;

    /// <summary>两排布局开关（价格梯子双排显示）。</summary>
    [ObservableProperty] private bool _twoRowLayout;

    /// <summary>窗口置顶开关（Topmost，对齐 0527.exe 配置按钮控制）。</summary>
    [ObservableProperty] private bool _alwaysOnTop;

    /// <summary>浮动栏「仓/平」改变时，只影响已创建的合约窗口。</summary>
    partial void OnOrderModeChanged(FloatingOrderMode value) =>
        _tradingWindowInteraction.ApplyOnlyOpenToOpenWindows(value == FloatingOrderMode.Open);

    /// <summary>浮动栏 A/B 动作对应旧版遍历全局 TYYWin 列表逐个勾选 RBOA/RBOB。</summary>
    partial void OnAbModeChanged(FloatingAbMode value) =>
        _tradingWindowInteraction.ApplyOrderPlacementModeToOpenWindows(
            value == FloatingAbMode.A ? OrderPlacementMode.ReplaceSameDirection : OrderPlacementMode.Append);

    /// <summary>窗口同步模式（成组联动 / 完全独立）。</summary>
    [ObservableProperty] private WindowSyncMode _syncMode = WindowSyncMode.Grouped;

    /// <summary>
    /// <see cref="SyncMode"/> 变更后通知 <see cref="IsSyncGrouped"/> 重新计算，
    /// 让 <c>StateToggleButton.IsChecked</c> 双向绑定保持同步（例如通过
    /// <see cref="ToggleSyncModeCommand"/> 切换后按钮状态即时刷新）。
    /// </summary>
    partial void OnSyncModeChanged(WindowSyncMode value) => OnPropertyChanged(nameof(IsSyncGrouped));

    /// <summary>
    /// 同步模式开关：true=成组联动（Grouped）/ false=完全独立（Independent）。
    /// <para>
    /// 派生自 <see cref="SyncMode"/>，供 <c>StateToggleButton.IsChecked</c> 直接 TwoWay 绑定，
    /// 避免在 XAML 端引入 EnumToBoolConverter。setter 写回 <see cref="SyncMode"/> 并同步到
    /// <see cref="GroupSynchronizationCoordinator"/>。
    /// </para>
    /// </summary>
    public bool IsSyncGrouped
    {
        get => SyncMode == WindowSyncMode.Grouped;
        set
        {
            var next = value ? WindowSyncMode.Grouped : WindowSyncMode.Independent;
            if (SyncMode == next) return;
            SyncMode = next;
            _sync.SyncMode = next;
            OnPropertyChanged();
        }
    }

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
            // 关闭窗口会回写 Users.xml；每次切组重新读，避免拿旧快照覆盖刚持久化的 A/B/仓平设置。
            _layout = _groupService.Load();
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
            // 始终从持久化布局合并，不能用初始化时的旧快照覆盖窗口关闭后的回写。
            _layout = _groupService.Load();
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
