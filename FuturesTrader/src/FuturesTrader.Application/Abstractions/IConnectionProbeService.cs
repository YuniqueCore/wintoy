using FuturesTrader.Domain.Connections;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 连接测速服务：TCP 连接到目标 host:port 测往返延迟。
/// 用于登录页行情/交易地址列表的延迟色块展示（绿 &lt;30ms / 黄 &lt;60ms / 红 ≥60ms / 灰不可达）。
/// </summary>
public interface IConnectionProbeService
{
    /// <summary>
    /// 探测单个端点延迟。
    /// </summary>
    /// <param name="host">目标主机。</param>
    /// <param name="port">目标端口。</param>
    /// <param name="timeout">单次探测超时。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 并发探测多个端点，逐个回调结果（用于 UI 实时刷新延迟色块）。
    /// 每条独立 try/catch，不因一个失败拖累其他。
    /// </summary>
    Task ProbeAllAsync(IReadOnlyList<(string Host, int Port)> endpoints, TimeSpan timeout, Action<ProbeResult> onResult, CancellationToken cancellationToken = default);
}
