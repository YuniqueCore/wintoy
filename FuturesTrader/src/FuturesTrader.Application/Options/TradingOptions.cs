using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Options;

/// <summary>
/// 交易服务配置选项，从 appsettings.json 的 "Trading" 节绑定。
/// <see cref="Provider"/> 决定 DI 工厂装配 Mock 或 Ctp 实现；CTP 字段在 Mock 模式下可留空。
/// 对齐 SimNow/openctp 凭据：BrokerID/UserID/Password/AppID/AuthCode（认证→登录→结算确认三步）。
/// </summary>
public sealed class TradingOptions
{
    /// <summary>交易服务实现选型：Mock（本地模拟）/ Ctp（直连 thosttraderapi_se.dll）。</summary>
    public TradingProvider Provider { get; init; } = TradingProvider.Mock;

    /// <summary>CTP 交易前置地址，如 tcp://180.168.146.187:10201（SimNow 7×24）。</summary>
    public string FrontAddress { get; init; } = string.Empty;

    /// <summary>经纪商代码，如 88888（SimNow）。</summary>
    public string BrokerId { get; init; } = string.Empty;

    /// <summary>用户 ID（投资者账号）。</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>登录密码（SimNow 默认与账号同号，生产环境由用户输入）。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>客户端认证 AppID（如 Weg_yiyisy_V1.0，CTP 6.5+ 强制认证）。</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>认证授权码（AuthCode，与 AppID 配对，由期货公司分配）。</summary>
    public string AuthCode { get; init; } = string.Empty;

    /// <summary>CTP 交易流文件目录（CTP 要求可写，存报单流水等）。PostConfigure 会将相对路径绝对化。</summary>
    public string FlowPath { get; set; } = "./TraderFlow/";

    /// <summary>用户产品信息（UserProductInfo，CTP 认证字段，部分期货公司校验）。</summary>
    public string UserProductInfo { get; init; } = string.Empty;
}
