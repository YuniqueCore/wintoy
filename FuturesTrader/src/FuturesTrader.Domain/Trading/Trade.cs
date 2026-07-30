namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 成交回报值对象：CTP <c>OnRtnTrade</c> → 映射为不可变 record 推送到 <see cref="ITradingService.TradeStream"/>。
/// 每笔成交（部分成交/全部成交）CTP 推送一个 <c>CThostFtdcTradeField</c>。
/// </summary>
public sealed record Trade
{
    /// <summary>成交编号（交易所分配，同一笔成交在查询和回报中一致）。</summary>
    public string TradeId { get; init; } = string.Empty;

    /// <summary>报单引用（关联 <see cref="OrderResult.OrderRef"/>）。</summary>
    public string OrderRef { get; init; } = string.Empty;

    /// <summary>合约代码。</summary>
    public string InstrumentId { get; init; } = string.Empty;

    /// <summary>买卖方向。</summary>
    public Direction Direction { get; init; }

    /// <summary>开平标志。</summary>
    public OffsetFlag OffsetFlag { get; init; }

    /// <summary>成交价格。</summary>
    public decimal Price { get; init; }

    /// <summary>成交数量（手数）。</summary>
    public int Volume { get; init; }

    /// <summary>成交时间（HH:mm:ss）。</summary>
    public TimeOnly TradeTime { get; init; }

    /// <summary>交易日（CTP TradingDay）。</summary>
    public string TradingDay { get; init; } = string.Empty;

    /// <summary>交易所成交编号。</summary>
    public string ExchangeId { get; init; } = string.Empty;
}
