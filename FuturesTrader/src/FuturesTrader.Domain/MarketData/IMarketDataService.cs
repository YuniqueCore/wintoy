using System.Collections.Generic;

namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 行情服务抽象：统一 Mock（<c>SimulatedMarketDataService</c>）与 CTP
/// （<c>CtpMarketDataService</c>，直连 <c>thostmduserapi_se.dll</c> 6.7.10）两套实现。
/// 实现 MUST 线程安全：行情回调在 CTP 工作线程触发，订阅者（VM）需自行 Dispatcher 切回 UI 线程。
/// <see cref="MarketDataStream"/> 与 <see cref="ConnectionStream"/> 为冷/热流由实现决定，
/// 订阅者应假设热流（订阅即收当前快照 + 后续增量），且可重入。
/// </summary>
public interface IMarketDataService : IAsyncDisposable
{
    /// <summary>当前连接状态（用于 UI 反馈）。未连接时为 <see cref="ConnectionState.Disconnected"/>。</summary>
    ConnectionState CurrentState { get; }

    /// <summary>行情推送流：每个 tick 为一个合约的完整 <see cref="DepthMarketData"/> 快照。</summary>
    IObservable<DepthMarketData> MarketDataStream { get; }

    /// <summary>连接状态变更流（Disconnected→Connecting→Connected 等）。</summary>
    IObservable<ConnectionState> ConnectionStream { get; }

    /// <summary>建立行情连接（内部完成 RegisterFront/Init/OnRspUserLogin，CTP 实现可能阻塞至登录成功）。</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接并释放底层资源（CTP Release）。可重复调用。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>订阅指定合约集合的行情（幂等：已订阅合约不重复订阅）。</summary>
    Task SubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default);

    /// <summary>退订指定合约集合的行情（幂等）。</summary>
    Task UnsubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default);
}
