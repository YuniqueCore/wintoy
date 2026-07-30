namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 合约元数据值对象：对齐 CTP <c>CThostFtdcInstrumentField</c> 的关键字段。
/// <see cref="PriceTick"/> 是最小变动价位，价差居中价格梯以它为步长生成。
/// </summary>
public sealed record Instrument
{
    /// <summary>合约代码，如 ag2608。</summary>
    public string InstrumentId { get; init; } = string.Empty;

    /// <summary>交易所代码，如 SHFE。</summary>
    public string ExchangeId { get; init; } = string.Empty;

    /// <summary>合约名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>最小变动价位（每跳价格差）。</summary>
    public decimal PriceTick { get; init; }

    /// <summary>合约乘数（手数 × 价格 → 成交金额 的乘数）。</summary>
    public int VolumeMultiple { get; init; }
}
