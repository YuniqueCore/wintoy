namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 价差居中价格梯值对象：以最新价为中心、按 <see cref="PriceTick"/> 步长对称展开的买卖盘视图。
/// <see cref="Levels"/> = 上下各 N 档，<see cref="Rows"/> 共 2N+1 行（上方卖盘递减 → 中心最新价 → 下方买盘递减）。
/// 不可变；行情刷新时整体替换。由 <see cref="DepthMarketData.ToPriceLadder"/> 构造。
/// </summary>
public sealed record PriceLadder
{
    /// <summary>上下各多少档（N）。</summary>
    public int Levels { get; }

    /// <summary>中心最新价。</summary>
    public decimal LastPrice { get; }

    /// <summary>价格步长（最小变动价位）。</summary>
    public decimal PriceTick { get; }

    /// <summary>2N+1 行：上方卖盘(递减) → 中心 → 下方买盘(递减)。</summary>
    public IReadOnlyList<PriceLevel> Rows { get; }

    public PriceLadder(int levels, decimal lastPrice, decimal priceTick, IReadOnlyList<PriceLevel> rows)
    {
        Levels = levels;
        LastPrice = lastPrice;
        PriceTick = priceTick;
        Rows = rows;
    }

    /// <summary>中心行索引（<see cref="Levels"/>）。</summary>
    public int CenterIndex => Levels;

    /// <summary>中心行（最新价行）。</summary>
    public PriceLevel? Center => Rows.Count > CenterIndex ? Rows[CenterIndex] : null;
}
