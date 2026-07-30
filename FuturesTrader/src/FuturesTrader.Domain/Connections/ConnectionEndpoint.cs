namespace FuturesTrader.Domain.Connections;

/// <summary>
/// 连接端点值对象：行情/交易服务工厂的统一输入，封装 CTP 连接所需的全部凭据。
/// 登录时由 LoginViewModel 从用户选择的账号 + 行情地址 + 密码组装，传给工厂创建服务实例。
/// </summary>
public sealed record ConnectionEndpoint
{
    /// <summary>前置地址（tcp://host:port）。</summary>
    public string FrontAddress { get; init; } = string.Empty;

    /// <summary>经纪商代码。</summary>
    public string BrokerId { get; init; } = string.Empty;

    /// <summary>用户代码。</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>登录密码（明文，仅用于 CTP 连接，不持久化）。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>终端标识（AppID）。</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>认证码（AuthCode）。</summary>
    public string AuthCode { get; init; } = string.Empty;

    /// <summary>用户产品信息（交易专用，可空）。</summary>
    public string UserProductInfo { get; init; } = string.Empty;

    /// <summary>流文件存储目录（CTP 要求，如 ./MdFlow/ 或 ./TraderFlow/）。</summary>
    public string FlowPath { get; init; } = string.Empty;
}
