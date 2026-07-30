namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 价差居中价格梯的一行：某个价位上的买量、卖量，以及是否为最新价中心行。
/// 上方行（卖盘）<see cref="AskVolume"/> 有值，下方行（买盘）<see cref="BidVolume"/> 有值，
/// 中心行 <see cref="IsLastPrice"/> 为 true。
/// </summary>
public sealed record PriceLevel
{
    /// <summary>该档价位。</summary>
    public decimal Price { get; init; }

    /// <summary>买盘挂单量（下方行有值）。</summary>
    public int BidVolume { get; init; }

    /// <summary>卖盘挂单量（上方行有值）。</summary>
    public int AskVolume { get; init; }

    /// <summary>是否为最新价中心行。</summary>
    public bool IsLastPrice { get; init; }
}
