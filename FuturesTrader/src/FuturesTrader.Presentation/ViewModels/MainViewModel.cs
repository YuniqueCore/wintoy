using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 主窗口 ViewModel：加载 config.ini → 展示 CloudConfig → 可编辑保存。
/// 编排 Load/Save 命令，状态机驱动 UI。保留完整 CloudConfig，
/// 保存时仅替换 Window 段，保证 Order/User 段原值不丢。
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
        ILogger<MainViewModel> logger)
    {
        _repo = repo;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Window 段可编辑视图状态。</summary>
    public WindowConfigViewModel Window { get; } = new();

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
            State = new ConfigEditorState.Loaded();
            _logger.LogInformation("配置已加载: {Path}", _options.Path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载配置失败: {Path}", _options.Path);
            State = new ConfigEditorState.Error(ex.Message);
        }
    }

    /// <summary>保存配置到原路径。必须先 Load。</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loadedConfig is null) return;
        State = new ConfigEditorState.Saving();
        try
        {
            var config = _loadedConfig with { Window = Window.ToConfig(_loadedConfig.Window) };
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
