using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Presentation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wpf.Ui.Appearance;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 设置窗口 ViewModel：整合 config.ini 三段编辑（Window/Order/User）+ 窗口分组管理 + 外观主题切换 + 交易账号 CRUD。
/// <para>
/// 设计原则：<b>启动即加载最新配置</b>（无需用户点加载按钮）；保存按钮显式触发落盘。
/// 段落索引：0=Window, 1=Order, 2=User, 3=窗口分组, 4=交易账号, 5=外观。
/// </para>
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IConfigRepository _repo;
    private readonly ConfigFileOptions _options;
    private readonly IThemeService _theme;
    private readonly ILogger<SettingsViewModel> _logger;
    private CloudConfig? _loadedConfig;

    public SettingsViewModel(
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options,
        WindowGroupBarViewModel windowGroups,
        UserAccountEditorViewModel accounts,
        IThemeService theme,
        ILogger<SettingsViewModel> logger)
    {
        _repo = repo;
        _options = options.Value;
        _theme = theme;
        _logger = logger;
        WindowGroups = windowGroups;
        Accounts = accounts;
        // 订阅 Accounts.State 变化（交易账号段加载/保存/错误时刷新 CurrentState）
        accounts.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UserAccountEditorViewModel.State))
                OnPropertyChanged(nameof(CurrentState));
        };
        // 默认展示 Window 段
        CurrentSegment = Window;
        // 同步当前主题到 UI 选中态
        SelectedTheme = _theme.Current;
        // 启动即自动加载最新 config.ini（不需要用户点加载按钮）
        _ = LoadAsync();
    }

    /// <summary>Window 段可编辑视图状态。</summary>
    public WindowConfigViewModel Window { get; } = new();

    /// <summary>Order 段可编辑视图状态。</summary>
    public OrderConfigViewModel Order { get; } = new();

    /// <summary>User 段可编辑视图状态。</summary>
    public UserConfigViewModel User { get; } = new();

    /// <summary>窗口分组管理段视图状态（20 个分组 + 绑定/解绑/重命名/一键开组）。</summary>
    public WindowGroupBarViewModel WindowGroups { get; }

    /// <summary>交易账号管理段视图状态（CRUD + 列表编辑）。</summary>
    public UserAccountEditorViewModel Accounts { get; }

    /// <summary>当前侧边栏选中的段索引（0=Window, 1=Order, 2=User, 3=窗口分组, 4=交易账号, 5=外观）。</summary>
    [ObservableProperty]
    public partial int CurrentSectionIndex { get; set; }

    /// <summary>索引变更时同步切换 CurrentSegment，ContentControl 按运行时类型选 DataTemplate。</summary>
    partial void OnCurrentSectionIndexChanged(int value)
    {
        CurrentSegment = value switch
        {
            1 => Order,
            2 => User,
            3 => WindowGroups,
            4 => Accounts,
            5 => this, // 外观段直接绑 SettingsViewModel 自身的主题属性
            _ => Window
        };
        OnPropertyChanged(nameof(CurrentState));
        OnPropertyChanged(nameof(HasCurrentState));
    }

    /// <summary>State 变化时通知 CurrentState（确保状态栏实时刷新）。</summary>
    partial void OnStateChanged(ConfigEditorState value) => OnPropertyChanged(nameof(CurrentState));

    /// <summary>当前展示的段 VM，ContentControl 按其运行时类型选 DataTemplate。</summary>
    [ObservableProperty]
    public partial object CurrentSegment { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial ConfigEditorState State { get; private set; } = new ConfigEditorState.Loading();

    /// <summary>
    /// 当前段落对应的状态对象（用于底部状态栏统一展示）。
    /// 0-2 段用 SettingsViewModel.State（ConfigEditorState），3=窗口分组用 WindowGroups.State，
    /// 4=交易账号用 Accounts.State，5=外观段返回 null（无状态）。
    /// </summary>
    public object? CurrentState => CurrentSectionIndex switch
    {
        3 => WindowGroups.State,
        4 => Accounts.State,
        _ => State,
    };

    /// <summary>当前段是否有可显示的状态（5 段 = 外观 → 无状态）。</summary>
    public bool HasCurrentState => CurrentSectionIndex != 5;

    /// <summary>上次保存时间，UI 显示"已保存 HH:mm:ss"反馈。</summary>
    [ObservableProperty]
    public partial DateTime? LastSavedAt { get; set; }

    // ── 外观段：主题切换 ──

    /// <summary>当前选中的主题（Light/Dark）；设置时即时应用并持久化。</summary>
    [ObservableProperty]
    public partial ApplicationTheme SelectedTheme { get; set; }

    /// <summary>SelectedTheme 变更 → 即时应用主题（同步所有窗口）。</summary>
    partial void OnSelectedThemeChanged(ApplicationTheme value)
    {
        _theme.Apply(value);
        _logger.LogInformation("主题已切换：{Theme}", value);
    }

    /// <summary>是否为深色主题（UI RadioButton 绑定便利）。</summary>
    public bool IsDarkTheme
    {
        get => SelectedTheme == ApplicationTheme.Dark;
        set
        {
            if (value) SelectedTheme = ApplicationTheme.Dark;
            OnPropertyChanged(nameof(IsLightTheme));
        }
    }

    /// <summary>是否为浅色主题（UI RadioButton 绑定便利）。</summary>
    public bool IsLightTheme
    {
        get => SelectedTheme == ApplicationTheme.Light;
        set
        {
            if (value) SelectedTheme = ApplicationTheme.Light;
            OnPropertyChanged(nameof(IsDarkTheme));
        }
    }

    /// <summary>
    /// 加载配置到 VM（公开：构造时自动调用一次，测试可显式重新加载以重置 VM 字段）。
    /// Loaded 状态可用。
    /// </summary>
    public async Task LoadAsync()
    {
        State = new ConfigEditorState.Loading();
        try
        {
            var config = await Task.Run(() => _repo.Load(_options.Path));
            _loadedConfig = config;
            Window.Hydrate(config.Window);
            Order.Hydrate(config.Order);
            User.Hydrate(config.User);
            State = new ConfigEditorState.Loaded();
            _logger.LogInformation("配置已自动加载: {Path}", _options.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载配置失败: {Path}", _options.Path);
            State = new ConfigEditorState.Error(ex.Message);
        }
    }

    /// <summary>保存配置到磁盘（全量三段）。Loaded 状态可用。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loadedConfig is null) return;
        State = new ConfigEditorState.Saving();
        try
        {
            var config = _loadedConfig with
            {
                Window = Window.ToConfig(_loadedConfig.Window),
                Order = Order.ToConfig(),
                User = User.ToConfig(_loadedConfig.User)
            };
            await Task.Run(() => _repo.Save(_options.Path, config));
            _loadedConfig = config;
            LastSavedAt = DateTime.Now;
            State = new ConfigEditorState.Loaded();
            _logger.LogInformation("配置已保存: {Path}", _options.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置失败: {Path}", _options.Path);
            State = new ConfigEditorState.Error(ex.Message);
        }
    }

    private bool CanSave() => State is ConfigEditorState.Loaded;
}
