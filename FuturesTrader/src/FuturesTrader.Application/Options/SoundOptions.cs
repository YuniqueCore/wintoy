namespace FuturesTrader.Application.Options;

/// <summary>
/// 提示音配置选项，从 appsettings.json 的 "Sound" 节绑定。
/// BasePath 指向 wav 文件目录（assets/sounds）。PostConfigure 会将相对路径绝对化。
/// </summary>
public sealed class SoundOptions
{
    /// <summary>wav 文件所在目录。PostConfigure 会将相对路径绝对化。</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>是否启用提示音（用户在 TSoundWin 关闭时置 false）。</summary>
    public bool Enabled { get; set; } = true;
}
