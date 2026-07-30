namespace FuturesTrader.Application.Options;

/// <summary>
/// 提示音配置选项，从 appsettings.json 的 "Sound" 节绑定。
/// BasePath 指向旧软件目录（含 Nomoney.wav / cashreg.wav / chimes.wav / Cancellation.wav）。
/// </summary>
public sealed class SoundOptions
{
    /// <summary>wav 文件所在目录（旧软件根目录）。</summary>
    public string BasePath { get; init; } = string.Empty;

    /// <summary>是否启用提示音（用户在 TSoundWin 关闭时置 false）。</summary>
    public bool Enabled { get; init; } = true;
}
