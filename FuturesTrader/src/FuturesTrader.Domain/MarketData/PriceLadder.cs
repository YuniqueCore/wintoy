namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 买卖边界驱动的价格梯值对象：按 <see cref="PriceTick"/> 从卖方向买方连续展开。
/// 卖方和买方行数分别配置，二者之间的 <see cref="UnquotedRowCount"/> 由价差自动决定。
/// 不可变；行情刷新时整体替换。由 <see cref="DepthMarketData.ToPriceLadder"/> 构造。
/// </summary>
public sealed record PriceLadder
{
    /// <summary>中心最新价。</summary>
    public decimal LastPrice { get; }

    /// <summary>价格步长（最小变动价位）。</summary>
    public decimal PriceTick { get; }

    /// <summary>高价卖方 → 白格 → 低价买方，按价格严格降序。</summary>
    public IReadOnlyList<PriceLevel> Rows { get; }

    public PriceLadder(decimal lastPrice, decimal priceTick, IReadOnlyList<PriceLevel> rows)
    {
        LastPrice = lastPrice;
        PriceTick = priceTick;
        Rows = rows;
    }

    public int AskQuoteRowCount => Rows.Count(row => row.DisplayZone == PriceDisplayZone.AskQuote);

    public int BidQuoteRowCount => Rows.Count(row => row.DisplayZone == PriceDisplayZone.BidQuote);

    public int UnquotedRowCount => Rows.Count(row => row.DisplayZone == PriceDisplayZone.Unquoted);

    /// <summary>最新价行的真实索引；最新价不在当前可见范围时为 -1。</summary>
    public int CenterIndex
    {
        get
        {
            for (var index = 0; index < Rows.Count; index++)
            {
                if (Rows[index].IsLastPrice) return index;
            }
            return -1;
        }
    }

    /// <summary>最新价行。</summary>
    public PriceLevel? Center => CenterIndex >= 0 ? Rows[CenterIndex] : null;
}
