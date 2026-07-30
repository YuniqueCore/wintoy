using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Application.Options;

/// <summary>
/// 行情服务配置选项，从 appsettings.json 的 "MarketData" 节绑定。
/// <see cref="Provider"/> 决定 DI 工厂装配 Mock 或 Ctp 实现；CTP 字段在 Mock 模式下可留空。
/// FrontAddress/BrokerId/UserId/Password/AppId/AuthCode 对齐 SimNow/openctp 凭据；
/// FlowPath 是 CTP 行情流文件目录（CTP 要求可写）。
/// </summary>
public sealed class MarketDataOptions
{
    /// <summary>行情服务实现选型：Mock（模拟随机游走）/ Ctp（直连 thostmduserapi_se.dll）。</summary>
    public MarketDataProvider Provider { get; init; } = MarketDataProvider.Mock;

    /// <summary>CTP 行情前置地址，如 tcp://180.168.146.187:10131（SimNow 7×24）。</summary>
    public string FrontAddress { get; init; } = string.Empty;

    /// <summary>经纪商代码，如 88888（SimNow）。</summary>
    public string BrokerId { get; init; } = string.Empty;

    /// <summary>用户 ID（投资者账号）。</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>登录密码（SimNow 默认与账号同号，生产环境由用户输入）。</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>客户端认证 AppID（如 Weg_yiyisy_V1.0）。</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>认证授权码（AuthCode）。</summary>
    public string AuthCode { get; init; } = string.Empty;

    /// <summary>CTP 行情流文件目录（CTP 要求可写，存订阅状态等）。PostConfigure 会将相对路径绝对化。</summary>
    public string FlowPath { get; set; } = "./MdFlow/";

    /// <summary>价差居中价格梯上下档位数（默认 5，对齐 CTP 5 档买卖盘）。</summary>
    public int PriceLadderLevels { get; init; } = 5;

    /// <summary>Mock 模式下 tick 推送间隔（毫秒，默认 500）。</summary>
    public int MockTickIntervalMs { get; init; } = 500;
}
