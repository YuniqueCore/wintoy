using System.Diagnostics;
using System.Net.Sockets;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Network;

/// <summary>
/// TCP 连接测速服务：用 <see cref="TcpClient.ConnectAsync"/> 测往返延迟。
/// 用于登录页行情/交易地址列表的延迟色块展示（绿 &lt;30ms / 黄 &lt;60ms / 红 ≥60ms / 灰不可达）。
/// <para>每条探测独立 try/catch + 超时取消，不因一个失败拖累其他。</para>
/// </summary>
public sealed class TcpConnectionProbeService : IConnectionProbeService
{
    private readonly ILogger<TcpConnectionProbeService> _logger;

    public TcpConnectionProbeService(ILogger<TcpConnectionProbeService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ProbeResult> ProbeAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        var sw = Stopwatch.StartNew();

        try
        {
            await tcp.ConnectAsync(host, port, cts.Token);
            sw.Stop();
            return ProbeResult.Ok(host, port, sw.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            return ProbeResult.Fail(host, port, $"超时（{timeout.TotalMilliseconds:F0}ms）");
        }
        catch (Exception ex)
        {
            return ProbeResult.Fail(host, port, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task ProbeAllAsync(
        IReadOnlyList<(string Host, int Port)> endpoints,
        TimeSpan timeout,
        Action<ProbeResult> onResult,
        CancellationToken cancellationToken = default)
    {
        var tasks = endpoints.Select(async ep =>
        {
            var result = await ProbeAsync(ep.Host, ep.Port, timeout, cancellationToken);
            onResult(result);
        });
        await Task.WhenAll(tasks);
    }
}
