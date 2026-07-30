namespace FuturesTrader.Domain.Connections;

/// <summary>
/// 行情上游地址条目：镜像 HQAddress.xml 的 &lt;Address&gt; 元素。
/// <para>示例 XML：<c>&lt;Address Name="海通" Port="38215"&gt;180.168.212.75&lt;/Address&gt;</c></para>
/// </summary>
public sealed record HqAddressEntry
{
    /// <summary>行情服务商简称（如"海通"、"东证联通"）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>行情服务器 IP 地址（XML 元素文本）。</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>行情服务器端口（XML Port 属性）。</summary>
    public int Port { get; init; }

    /// <summary>完整 TCP 连接地址：<c>tcp://{Host}:{Port}</c>。</summary>
    public string Url => $"tcp://{Host}:{Port}";

    /// <summary>延迟（毫秒），由测速服务填充；null 表示尚未测速。</summary>
    public double? LatencyMs { get; init; }

    /// <summary>测速是否成功（不可达时为 false）。</summary>
    public bool ProbeSuccess { get; init; } = true;
}
