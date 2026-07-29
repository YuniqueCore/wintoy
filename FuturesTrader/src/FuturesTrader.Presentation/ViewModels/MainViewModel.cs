using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 主窗口 ViewModel：加载 config.ini → 展示三段配置（Window/Order/User）→ 可编辑保存。
/// 编排 Load/Save 命令，状态机驱动 UI。
/// <see cref="CurrentSegment"/> 持有当前 NavigationView 选中的段 VM，
/// 由 ContentControl 按运行时类型选 DataTemplate 渲染（零 Converter）。
/// 保存时全量 with 替换三段，保证不丢字段。
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IConfigRepository _repo;
    private readonly ConfigFileOptions _options;
    private readonly ILogger<MainViewModel> _logger;
    private CloudConfig? _loadedConfig;

    public MainViewModel(
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options,
        WindowGroupBarViewModel windowGroups,
        ILogger<MainViewModel> logger)
    {
        _repo = repo;
        _options = options.Value;
        _logger = logger;
        WindowGroups = windowGroups;
        // 默认展示 Window 段
        CurrentSegment = Window;
    }

    /// <summary>Window 段可编辑视图状态。</summary>
    public WindowConfigViewModel Window { get; } = new();

    /// <summary>Order 段可编辑视图状态。</summary>
    public OrderConfigViewModel Order { get; } = new();

    /// <summary>User 段可编辑视图状态。</summary>
    public UserConfigViewModel User { get; } = new();

    /// <summary>窗口分组管理段视图状态（20 个分组 + 绑定/解绑/重命名/一键开组）。</summary>
    public WindowGroupBarViewModel WindowGroups { get; }

    /// <summary>当前侧边栏选中的段索引（0=Window, 1=Order, 2=User, 3=窗口分组）。</summary>
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
