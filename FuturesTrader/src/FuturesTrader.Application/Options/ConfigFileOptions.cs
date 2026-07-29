namespace FuturesTrader.Application.Options;

/// <summary>
/// 配置文件路径选项，从 appsettings.json 的 "ConfigFile" 节绑定。
/// 由 Host 层在 DI 容器中注册，MainViewModel 通过 IOptions 注入读取。
/// </summary>
public sealed class ConfigFileOptions
{
    /// <summary>旧软件 config.ini 的绝对路径（GBK 编码）。</summary>
    public string Path { get; init; } = string.Empty;
}
