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
/// 设置窗口 ViewModel：整合 config.ini 三段编辑（Window/Order/User）+ 窗口分组管理 + 外观主题切换。
/// <para>
/// 继承 MainViewModel 的编排逻辑（Load/Save/段落切换/状态机），新增「外观」段：
/// 主题 Light/Dark 选择，通过 <see cref="IThemeService"/> 即时应用并持久化到 user-settings.json。
/// </para>
/// <para>
/// 段落索引：0=Window, 1=Order, 2=User, 3=窗口分组, 4=外观。
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
        IThemeService theme,
        ILogger<SettingsViewModel> logger)
    {
        _repo = repo;
        _options = options.Value;
        _theme = theme;
        _logger = logger;
        WindowGroups = windowGroups;
        // 默认展示 Window 段
        CurrentSegment = Window;
        // 同步当前主题到 UI 选中态
        SelectedTheme = _theme.Current;
    }

    /// <summary>Window 段可编辑视图状态。</summary>
    public WindowConfigViewModel Window { get; } = new();

    /// <summary>Order 段可编辑视图状态。</summary>
    public OrderConfigViewModel Order { get; } = new();

    /// <summary>User 段可编辑视图状态。</summary>
    public UserConfigViewModel User { get; } = new();

    /// <summary>窗口分组管理段视图状态（20 个分组 + 绑定/解绑/重命名/一键开组）。</summary>
    public WindowGroupBarViewModel WindowGroups { get; }

    /// <summary>当前侧边栏选中的段索引（0=Window, 1=Order, 2=User, 3=窗口分组, 4=外观）。</summary>
    [ObservableProperty]
    public partial int CurrentSectionIndex { get; set; }

    /// <summary>索引变更时同步切换 CurrentSegment，ContentControl 按运行时类型选 DataTemplate。
    /// 切到「窗口分组」段时首次自动加载（EnsureLoaded）。</summary>
    partial void OnCurrentSectionIndexChanged(int value)
    {
        CurrentSegment = value switch
        {
            1 => Order,
            2 => User,
            3 => WindowGroups,
            4 => this, // 外观段直接绑 SettingsViewModel 自身的主题属性
            _ => Window
        };
        if (value == 3) WindowGroups.EnsureLoaded();
    }

    /// <summary>当前展示的段 VM，ContentControl 按其运行时类型选 DataTemplate。</summary>
    [ObservableProperty]
    public partial object CurrentSegment { get; set; }

    [ObservableProperty]
    public partial ConfigEditorState State { get; private set; } = new ConfigEditorState.Idle();

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

    /// <summary>加载配置文件。Loading/Loaded/Error 状态由编译器保证不可重叠。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadOrSave))]
    private async Task LoadAsync()
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
            _logger.LogInformation("配置已加载: {Path}", _options.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载配置失败: {Path}", _options.Path);
            State = new ConfigEditorState.Error(ex.Message);
        }
    }

    /// <summary>保存配置到原路径（全量三段）。必须先 Load。</summary>
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

    private bool CanLoadOrSave() =>
        State is ConfigEditorState.Idle or ConfigEditorState.Loaded or ConfigEditorState.Error;

    private bool CanSave() => State is ConfigEditorState.Loaded;
}
