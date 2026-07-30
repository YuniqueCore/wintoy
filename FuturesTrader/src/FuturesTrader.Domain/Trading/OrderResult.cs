namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 报单回报值对象：CTP <c>OnRtnOrder</c> → 映射为不可变 record 推送到 <see cref="ITradingService.OrderStream"/>。
/// 每次报单状态变化（接受/部分成交/撤单/拒绝）CTP 都会推送一个完整快照。
/// </summary>
public sealed record OrderResult
{
    /// <summary>报单引用（与会话内 <see cref="OrderRequest.OrderRef"/> 对应）。</summary>
    public string OrderRef { get; init; } = string.Empty;

    /// <summary>前置 ID（CTP FrontID，与 SessionID + OrderRef 三元组唯一标识报单）。</summary>
    public int FrontId { get; init; }

    /// <summary>会话 ID（CTP SessionID）。</summary>
    public int SessionId { get; init; }

    /// <summary>交易所报单编号（交易所返回，查询/撤单用）。</summary>
    public string ExchangeId { get; init; } = string.Empty;

    /// <summary>合约代码。</summary>
    public string InstrumentId { get; init; } = string.Empty;

    /// <summary>买卖方向。</summary>
    public Direction Direction { get; init; }

    /// <summary>开平标志。</summary>
    public OffsetFlag OffsetFlag { get; init; }

    /// <summary>申报价格。</summary>
    public decimal Price { get; init; }

    /// <summary>申报数量（原始报单手数）。</summary>
    public int Volume { get; init; }

    /// <summary>已成交数量。</summary>
    public int VolumeTraded { get; init; }

    /// <summary>剩余数量（= Volume - VolumeTraded，CTP 字段 VolumeTotal）。</summary>
    public int VolumeRemaining { get; init; }

    /// <summary>当前状态（状态机）。</summary>
    public OrderStatus Status { get; init; } = new OrderStatus.Pending();

    /// <summary>报单时间（HH:mm:ss）。</summary>
    public TimeOnly InsertTime { get; init; }

    /// <summary>状态消息（CTP StatusMsg，GBK 解码后的中文说明）。</summary>
    public string StatusMessage { get; init; } = string.Empty;
}
