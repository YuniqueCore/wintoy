namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 报单状态机（对齐 CTP <c>TThostFtdcOrderStatusType</c>，合并为业务可用的状态集合）。
/// 密封子类保证"非法状态无法表达"（参考 <c>ConnectionState</c> 模式）。
/// <para>
/// CTP 原始状态码映射：
/// <list type="bullet">
///   <item>'a' Unknown → <see cref="Pending"/>（已提交，等待交易所确认）</item>
///   <item>'b' NotTouched / 'c' Touched → <see cref="Accepted"/>（交易所已接受，排队中）</item>
///   <item>'1' PartTradedQueueing → <see cref="PartiallyFilled"/>（部分成交，剩余排队）</item>
///   <item>'0' AllTraded → <see cref="Filled"/>（全部成交）</item>
///   <item>'5' Canceled → <see cref="Canceled"/>（已撤单）</item>
///   <item>'2'/'4' NotQueueing → <see cref="Canceled"/>（部分成交后撤/全撤且不排队）</item>
/// </list>
/// <see cref="Rejected"/> 用于 OnRspOrderInsert/OnErrRtnOrderInsert 报单录入被拒。
/// <see cref="Canceling"/> 是本地中间态（撤单请求已发但未收到 OnRtnOrder 确认）。
/// </para>
/// </summary>
public abstract record OrderStatus
{
    private OrderStatus() { }

    /// <summary>已提交，等待交易所确认（CTP Unknown）。</summary>
    public sealed record Pending : OrderStatus;

    /// <summary>交易所已接受，排队中（CTP NotTouched/Touched）。</summary>
    public sealed record Accepted : OrderStatus;

    /// <summary>部分成交，剩余量仍在排队（CTP PartTradedQueueing）。</summary>
    public sealed record PartiallyFilled(int FilledVolume) : OrderStatus;

    /// <summary>全部成交（CTP AllTraded）。</summary>
    public sealed record Filled(int FilledVolume) : OrderStatus;

    /// <summary>撤单请求已发送，等待回报确认（本地中间态）。</summary>
    public sealed record Canceling : OrderStatus;

    /// <summary>已撤单（CTP Canceled / NoTradeNotQueueing / PartTradedNotQueueing）。</summary>
    public sealed record Canceled(int FilledVolume) : OrderStatus;

    /// <summary>报单被拒（OnRspOrderInsert 错误或 OnErrRtnOrderInsert）。</summary>
    public sealed record Rejected(string Reason) : OrderStatus;
}
