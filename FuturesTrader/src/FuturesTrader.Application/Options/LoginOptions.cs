namespace FuturesTrader.Application.Options;

/// <summary>
/// 登录相关配置：映射 appsettings.json 的 Login 段。
/// </summary>
public sealed class LoginOptions
{
    /// <summary>HQAddress.xml 路径（行情上游地址列表）。</summary>
    public string HqAddressXmlPath { get; set; } = string.Empty;

    /// <summary>Users.xml 路径（多账号 + 窗口历史）。</summary>
    public string UsersXmlPath { get; set; } = string.Empty;

    /// <summary>config.ini 路径（全局配置：Window/Order/User 段）。</summary>
    public string ConfigIniPath { get; set; } = string.Empty;

    /// <summary>TCP 测速单次超时（毫秒）。</summary>
    public int ProbeTimeoutMs { get; set; } = 3000;

    /// <summary>CTP 连接超时（秒）。</summary>
    public int ConnectTimeoutSec { get; set; } = 15;

    /// <summary>是否使用 Mock 模式（离线开发，跳过真实 CTP 连接）。</summary>
    public bool UseMock { get; set; }
}
