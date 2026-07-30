using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 交易服务抽象：统一 Mock（<c>MockTradingService</c>）与 CTP
/// （<c>CtpTradingService</c>，直连 <c>thosttraderapi_se.dll</c> 6.7.10）两套实现。
/// 实现 MUST 线程安全：CTP 回调在工作线程触发，订阅者（VM）需自行 Dispatcher 切回 UI 线程。
/// <para>
/// 认证流程（CTP 独有）：OnFrontConnected → ReqAuthenticate(BrokerID/UserID/AppID/AuthCode) →
/// OnRspAuthenticate → ReqUserLogin(BrokerID/UserID/Password) → OnRspUserLogin →
/// ReqSettlementInfoConfirm → OnRspSettlementInfoConfirm → Connected。
/// </para>
/// <para>
/// <see cref="OrderStream"/> 推送报单状态变更（含录入响应 OnRspOrderInsert + 报单通知 OnRtnOrder + 错误回报 OnErrRtnOrderInsert）。
/// <see cref="TradeStream"/> 推送成交通知（OnRtnTrade）。
/// </para>
/// </summary>
public interface ITradingService : IAsyncDisposable
{
    /// <summary>当前连接状态（复用 <see cref="ConnectionState"/> 状态机，与行情共用）。</summary>
    ConnectionState CurrentState { get; }

    /// <summary>报单回报流：每次报单状态变化推送一个 <see cref="OrderResult"/> 快照。</summary>
    IObservable<OrderResult> OrderStream { get; }

    /// <summary>成交回报流：每笔成交推送一个 <see cref="Trade"/>。</summary>
    IObservable<Trade> TradeStream { get; }

    /// <summary>连接状态变更流（Disconnected→Connecting→Connected 等）。</summary>
    IObservable<ConnectionState> ConnectionStream { get; }

    /// <summary>
    /// 建立交易连接（内部完成认证 ReqAuthenticate → 登录 ReqUserLogin → 结算确认 ReqSettlementInfoConfirm）。
    /// CTP 实现会阻塞至结算确认成功（带超时）。
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>断开连接并释放底层资源（CTP Release）。可重复调用。</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 发送报单（CTP ReqOrderInsert）。
    /// 返回的 Task 在报单请求提交完成时完成（不等成交回报）；
    /// 实际报单结果通过 <see cref="OrderStream"/> 异步推送。
    /// </summary>
    /// <param name="request">报单请求（方向/开平/价格/数量）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>报单引用（OrderRef），用于关联后续回报。失败抛异常。</returns>
    Task<string> SendOrderAsync(OrderRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 撤单（CTP ReqOrderAction）。
    /// 通过 <paramref name="orderRef"/> + <paramref name="frontId"/> + <paramref name="sessionId"/> 定位报单。
    /// 撤单结果通过 <see cref="OrderStream"/> 异步推送（状态变为 <see cref="OrderStatus.Canceled"/>）。
    /// </summary>
    Task CancelOrderAsync(string orderRef, int frontId, int sessionId, CancellationToken cancellationToken = default);
}
