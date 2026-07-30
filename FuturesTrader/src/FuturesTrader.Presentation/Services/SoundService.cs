using System.IO;
using System.Windows.Media;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// <see cref="ISoundService"/> 实现：用 WPF <see cref="MediaPlayer"/> 播放 4 种事件音效。
/// 单例，Dispatcher 线程安全（CTP 回调在工作线程触发 Play，MediaPlayer 必须在 UI 线程操作）。
/// 文件缺失/播放失败时静默降级并记日志，不抛异常（避免影响交易主流程）。
/// 放在 Presentation 层因 MediaPlayer 是 WPF 类型；<see cref="ISoundService"/> 接口在 Application 层（无 WPF 耦合）。
/// </summary>
public sealed class SoundService : ISoundService
{
    private static readonly Dictionary<SoundType, string> FileNames = new()
    {
        [SoundType.NoMoney] = "Nomoney.wav",
        [SoundType.CashRegister] = "cashreg.wav",
        [SoundType.Chimes] = "chimes.wav",
        [SoundType.Cancel] = "Cancellation.wav"
    };

    private readonly string _basePath;
    private readonly bool _enabledDefault;
    private readonly ILogger<SoundService> _logger;
    private readonly Dictionary<SoundType, MediaPlayer> _players = new();
    private readonly object _lock = new();

    public SoundService(IOptions<SoundOptions> options, ILogger<SoundService> logger)
    {
        _basePath = options.Value.BasePath ?? string.Empty;
        _enabledDefault = options.Value.Enabled;
        _logger = logger;
        Enabled = _enabledDefault;
    }

    /// <inheritdoc />
    public bool Enabled { get; set; }

    /// <inheritdoc />
    public void Play(SoundType type)
    {
        if (!Enabled) return;
        if (!FileNames.TryGetValue(type, out var fileName)) return;
        var path = Path.Combine(_basePath, fileName);
        if (!File.Exists(path))
        {
            _logger.LogWarning("音效文件不存在: {Path}", path);
            return;
        }

        // MediaPlayer 必须在 UI 线程创建与操作；若当前不在 UI 线程则通过 Dispatcher 切回。
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            // 无 WPF 应用上下文（如单元测试）— 静默跳过，不崩
            return;
        }
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(() => PlayInternal(type, path));
        }
        else
        {
            PlayInternal(type, path);
        }
    }

    private void PlayInternal(SoundType type, string path)
    {
        try
        {
            lock (_lock)
            {
                if (!_players.TryGetValue(type, out var player))
                {
                    player = new MediaPlayer();
                    _players[type] = player;
                }
                player.Open(new Uri(path, UriKind.Absolute));
                player.Position = TimeSpan.Zero;
                player.Play();
            }
        }
        catch (Exception ex)
        {
            // 静默降级：音效失败不应影响交易主流程
            _logger.LogWarning(ex, "播放音效失败: {Type} {Path}", type, path);
        }
    }
}
