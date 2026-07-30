namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 价差居中价格梯的一行：某个价位上的买量、卖量、用户本地挂单数，以及是否为最新价中心行。
/// 上方行（卖盘）<see cref="AskVolume"/> 有值，下方行（买盘）<see cref="BidVolume"/> 有值，
/// 中心行 <see cref="IsLastPrice"/> 为 true。
/// <para>
/// <see cref="PendingOrderCount"/> 是用户在当前会话挂到本档位的未成交报单数（0 表示无），
/// 由 <c>TradingViewModel</c> 在收到 <c>OrderResult</c> 回报时聚合到对应 <see cref="PriceLevel"/>。
/// UI 第 0 列显示此值，&gt;0 时高亮且支持点击撤单（对齐 0527.exe PriceList 第 1 列语义）。
/// </para>
/// </summary>
public sealed record PriceLevel
{
    /// <summary>该档价位。</summary>
    public decimal Price { get; init; }

    /// <summary>买盘挂单量（下方行有值）。</summary>
    public int BidVolume { get; init; }

    /// <summary>卖盘挂单量（上方行有值）。</summary>
    public int AskVolume { get; init; }

    /// <summary>本档位用户本地未成交报单数（活跃报单聚合，&gt;0 时 UI 高亮显示并可点击撤单）。</summary>
    public int PendingOrderCount { get; init; }

    /// <summary>是否为最新价中心行。</summary>
    public bool IsLastPrice { get; init; }

    /// <summary>所属区域：Ask(上方空单区)/Center/Bid(下方多单区)。由 ToPriceLadder 构造时设置。</summary>
    public PriceZone Zone { get; init; }
}
