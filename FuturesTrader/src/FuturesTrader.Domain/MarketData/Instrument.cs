namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 合约元数据值对象：对齐 CTP <c>CThostFtdcInstrumentField</c> 的关键字段。
/// <see cref="PriceTick"/> 是最小变动价位，价差居中价格梯以它为步长生成。
/// <para>
/// 期权字段（<see cref="ProductClass"/>/<see cref="StrikePrice"/>/<see cref="OptionsType"/>/<see cref="ExpireDate"/>）
/// 仅当 <see cref="IsOptions"/> 为 true 时有意义，用于构造 titlebar 显示如 "ps2609-C-36500 [10天 0807]"。
/// </para>
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

    /// <summary>限价单最小手数（来自 CTP <c>MinLimitOrderVolume</c>）。</summary>
    public int MinLimitOrderVolume { get; init; }

    /// <summary>限价单最大手数（来自 CTP <c>MaxLimitOrderVolume</c>）。</summary>
    public int MaxLimitOrderVolume { get; init; }

    /// <summary>交易所当前是否允许该合约交易（CTP <c>IsTrading</c>）。</summary>
    public bool IsTrading { get; init; }

    /// <summary>产品类型（CTP byte，ASCII 字符）：'1'=期货 '2'=期权 '3'=组合 '5'=现货 'F'=价差。</summary>
    public byte ProductClass { get; init; }

    /// <summary>执行价（期权专用，期货为 0）。</summary>
    public decimal StrikePrice { get; init; }

    /// <summary>期权类型（CTP byte，ASCII 字符）：'1'=Call '2'=Put。期货为 0。</summary>
    public byte OptionsType { get; init; }

    /// <summary>到期日 yyyyMMdd（如 20260807）。</summary>
    public string ExpireDate { get; init; } = string.Empty;

    /// <summary>上市日 yyyyMMdd。</summary>
    public string CreateDate { get; init; } = string.Empty;

    /// <summary>是否期权合约（ProductClass == '2'）。</summary>
    public bool IsOptions => ProductClass == (byte)'2';
}
