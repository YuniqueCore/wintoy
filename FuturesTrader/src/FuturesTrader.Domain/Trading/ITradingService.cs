using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 交易服务抽象：统一 Mock（<c>MockTradingService</c>）与 CTP
/// （<c>CtpTradingService</c>，直连 <c>thosttraderapi_se.dll</c> 6.7.11）两套实现。
/// 实现 MUST 线程安全：CTP 回调在工作线程触发，订阅者（VM）需自行 Dispatcher 切回 UI 线程。
/// <para>
/// 认证流程（CTP 独有）：OnFrontConnected → ReqAuthenticate(BrokerID/UserID/AppID/AuthCode) →
/// OnRspAuthenticate → ReqUserLogin(BrokerID/UserID/Password) → OnRspUserLogin →
/// ReqSettlementInfoConfirm → OnRspSettlementInfoConfirm → Connected。
/// </para>
/// <para>
/// <b>推送流</b>：
/// <list type="bullet">
///   <item><see cref="OrderStream"/> 报单状态变更（OnRspOrderInsert + OnRtnOrder + OnErrRtnOrderInsert）</item>
///   <item><see cref="TradeStream"/> 成交通知（OnRtnTrade）</item>
///   <item><see cref="PositionStream"/> 持仓查询回报（OnRspQryInvestorPosition，每次查询推送一批，<c>bIsLast=true</c> 为批次末尾）</item>
///   <item><see cref="InstrumentStream"/> 合约元数据查询回报（OnRspQryInstrument）</item>
///   <item><see cref="AccountStream"/> 资金账户查询回报（OnRspQryTradingAccount，单条）</item>
/// </list>
/// </para>
/// <para>
/// <b>查询流控</b>：CTP 要求查询请求间隔 ≥ 1 秒（<c>ReqQry*</c> 返回 -3 表示流控，需重试）。
/// 实现应在 <see cref="ConnectAsync"/> 完成后自动发起首次持仓 + 资金查询（为浮动栏提供初始数据）。
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

    /// <summary>持仓回报流：每次 <see cref="QueryPositionAsync"/> 触发一批推送（每条持仓一个 <see cref="Position"/>）。</summary>
    IObservable<Position> PositionStream { get; }

    /// <summary>合约元数据回报流：每次 <see cref="QueryInstrumentAsync"/> 触发一批推送。</summary>
    IObservable<Instrument> InstrumentStream { get; }

    /// <summary>资金账户回报流：每次 <see cref="QueryTradingAccountAsync"/> 触发一次推送（通常单条）。</summary>
    IObservable<TradingAccount> AccountStream { get; }

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

    /// <summary>
    /// 查询投资者持仓（CTP ReqQryInvestorPosition）。
    /// <paramref name="instrumentId"/> 留空查全量持仓；指定合约查单合约持仓。
    /// 结果通过 <see cref="PositionStream"/> 异步推送（一批多条，<c>bIsLast=true</c> 为批次末尾）。
    /// <para>CTP 流控：两次查询需间隔 ≥ 1 秒，否则返回 -3。实现内部应处理流控重试。</para>
    /// </summary>
    Task QueryPositionAsync(string? instrumentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询合约元数据（CTP ReqQryInstrument）。
    /// <paramref name="instrumentId"/> 留空查全量合约（CTP 会返回数千条，谨慎使用）。
    /// 结果通过 <see cref="InstrumentStream"/> 异步推送。
    /// </summary>
    Task QueryInstrumentAsync(string? instrumentId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询资金账户（CTP ReqQryTradingAccount）。
    /// 结果通过 <see cref="AccountStream"/> 异步推送（通常单条）。
    /// </summary>
    Task QueryTradingAccountAsync(CancellationToken cancellationToken = default);
}
