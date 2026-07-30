namespace FuturesTrader.Domain.Connections;

/// <summary>
/// 连接测速结果：TCP 连接到目标 host:port 的往返延迟。
/// 由 <c>IConnectionProbeService</c> 产出，供登录页延迟色块展示。
/// </summary>
public sealed record ProbeResult
{
    /// <summary>目标主机。</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>目标端口。</summary>
    public int Port { get; init; }

    /// <summary>往返延迟（毫秒）；连接失败时为 null。</summary>
    public double? RttMs { get; init; }

    /// <summary>连接是否成功。</summary>
    public bool Success { get; init; }

    /// <summary>失败时的错误信息；成功时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>构造成功结果。</summary>
    public static ProbeResult Ok(string host, int port, double rttMs) => new()
    {
        Host = host,
        Port = port,
        RttMs = rttMs,
        Success = true
    };

    /// <summary>构造失败结果。</summary>
    public static ProbeResult Fail(string host, int port, string error) => new()
    {
        Host = host,
        Port = port,
        Success = false,
        Error = error
    };
}
