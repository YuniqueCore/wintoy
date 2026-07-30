namespace FuturesTrader.Domain.Connections;

/// <summary>
/// 交易账号条目：镜像 Users.xml 顶层 &lt;User&gt; 元素的连接信息（不含 WindowHistory）。
/// <para>
/// XML 结构：
/// <code>
/// &lt;User&gt;
///   &lt;title&gt;338897&lt;/title&gt;
///   &lt;address&gt;tcp://122.224.130.77:42205&lt;/address&gt;
///   &lt;brokerid&gt;88888&lt;/brokerid&gt;
///   &lt;userid&gt;338897&lt;/userid&gt;
///   &lt;appid&gt;Weg_yiyisy_V1.0&lt;/appid&gt;
///   &lt;shouquan&gt;VLH1QX4FHIJ976UC&lt;/shouquan&gt;
///   &lt;WindowHistory&gt;...&lt;/WindowHistory&gt;
/// &lt;/User&gt;
/// </code>
/// </para>
/// </summary>
public sealed record AccountEntry
{
    /// <summary>账号显示名（Users.xml &lt;title&gt;，通常等于 UserId）。</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>交易前置地址（Users.xml &lt;address&gt;，如 tcp://122.224.130.77:42205）。</summary>
    public string TradingAddress { get; init; } = string.Empty;

    /// <summary>经纪商代码（Users.xml &lt;brokerid&gt;）。</summary>
    public string BrokerId { get; init; } = string.Empty;

    /// <summary>用户代码（Users.xml &lt;userid&gt;）。</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>终端标识（Users.xml &lt;appid&gt;）。</summary>
    public string AppId { get; init; } = string.Empty;

    /// <summary>认证码（Users.xml &lt;shouquan&gt;）。</summary>
    public string AuthCode { get; init; } = string.Empty;
}
