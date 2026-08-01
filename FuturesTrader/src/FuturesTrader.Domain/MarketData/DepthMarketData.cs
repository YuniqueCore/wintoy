namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 深度行情值对象：对齐 CTP <c>CThostFtdcDepthMarketDataField</c>（见 02-ctp-api.md §6）。
/// 仅保留 0527.exe 实际使用的字段（最新价/开盘/最高/最低/量/额/持仓/涨跌停/5 档买卖盘）。
/// 不可变 record，行情推送时整体替换（避免部分更新导致的脏读）。
/// </summary>
public sealed record DepthMarketData
{
    public string InstrumentId { get; init; } = string.Empty;
    public string TradingDay { get; init; } = string.Empty;

    public decimal LastPrice { get; init; }
    public decimal PreSettlementPrice { get; init; }
    public decimal OpenPrice { get; init; }
    public decimal HighestPrice { get; init; }
    public decimal LowestPrice { get; init; }
    public decimal Volume { get; init; }
    public decimal Turnover { get; init; }
    public decimal OpenInterest { get; init; }
    public decimal UpperLimitPrice { get; init; }
    public decimal LowerLimitPrice { get; init; }
    public decimal AveragePrice { get; init; }

    public TimeOnly UpdateTime { get; init; }
    public int UpdateMillisec { get; init; }

    /// <summary>5 档买价（BidPrice1 最近买盘）。</summary>
    public IReadOnlyList<decimal> BidPrices { get; init; } = Array.Empty<decimal>();

    /// <summary>5 档买量。</summary>
    public IReadOnlyList<int> BidVolumes { get; init; } = Array.Empty<int>();

    /// <summary>5 档卖价（AskPrice1 最近卖盘）。</summary>
    public IReadOnlyList<decimal> AskPrices { get; init; } = Array.Empty<decimal>();

    /// <summary>5 档卖量。</summary>
    public IReadOnlyList<int> AskVolumes { get; init; } = Array.Empty<int>();

    /// <summary>
    /// 以真实卖一/买一为边界生成价格梯：卖一向上生成 <paramref name="askQuoteRowCount"/> 行，
    /// 买一向下生成 <paramref name="bidQuoteRowCount"/> 行，二者之间的白格完全按价差自动生成。
    /// 5 档买卖盘只按真实价位填量，延伸区域绝不伪造盘口量。
    /// <para>
    /// <paramref name="pendingByPrice"/> 可选：把外部维护的「价格 → 用户本地挂单数」聚合传入，
    /// 让 <see cref="PriceLevel.PendingOrderCount"/> 在 UI 第 0 列直接显示（点击可撤单）。
    /// 留空时所有 <see cref="PriceLevel.PendingOrderCount"/> = 0。
    /// </para>
    /// </summary>
    public PriceLadder ToPriceLadder(
        decimal priceTick,
        int askQuoteRowCount,
        int bidQuoteRowCount,
        IReadOnlyDictionary<decimal, int>? pendingByPrice = null)
    {
        if (priceTick <= 0) priceTick = 1m;
        askQuoteRowCount = NormalizeRowCount(askQuoteRowCount);
        bidQuoteRowCount = NormalizeRowCount(bidQuoteRowCount);
        pendingByPrice ??= new Dictionary<decimal, int>();

        var bestAsk = FindBestQuotedPrice(AskPrices, AskVolumes, findMinimum: true);
        var bestBid = FindBestQuotedPrice(BidPrices, BidVolumes, findMinimum: false);
        var askAnchor = bestAsk ?? LastPrice + priceTick;
        var bidAnchor = bestBid ?? LastPrice - priceTick;
        var prices = new HashSet<decimal>();

        for (var index = askQuoteRowCount - 1; index >= 0; index--)
            prices.Add(askAnchor + index * priceTick);

        for (var price = askAnchor - priceTick; price > bidAnchor; price -= priceTick)
            prices.Add(price);

        for (var index = 0; index < bidQuoteRowCount; index++)
            prices.Add(bidAnchor - index * priceTick);

        var rows = prices
            .OrderDescending()
            .Select(price => CreateLevel(price, priceTick, bestAsk, bestBid, pendingByPrice))
            .ToArray();
        return new PriceLadder(LastPrice, priceTick, rows);
    }

    private PriceLevel CreateLevel(
        decimal price,
        decimal priceTick,
        decimal? bestAsk,
        decimal? bestBid,
        IReadOnlyDictionary<decimal, int> pendingByPrice)
    {
        var askVolume = VolumeAt(price, AskPrices, AskVolumes, priceTick);
        var bidVolume = VolumeAt(price, BidPrices, BidVolumes, priceTick);
        return new PriceLevel
        {
            Price = price,
            IsLastPrice = Math.Abs(price - LastPrice) < priceTick / 2m,
            AskVolume = askVolume,
            BidVolume = bidVolume,
            PendingOrderCount = LookupPending(pendingByPrice, price, priceTick),
            DisplayZone = ResolveDisplayZone(price, bestAsk, bestBid, askVolume, bidVolume)
        };
    }

    private static int NormalizeRowCount(int count) => count <= 0 ? 30 : Math.Min(count, 100);

    private static decimal? FindBestQuotedPrice(
        IReadOnlyList<decimal> prices,
        IReadOnlyList<int> volumes,
        bool findMinimum)
    {
        decimal? best = null;
        for (var index = 0; index < prices.Count && index < volumes.Count; index++)
        {
            var price = prices[index];
            if (price <= 0 || volumes[index] <= 0) continue;
            if (best is null || (findMinimum ? price < best : price > best)) best = price;
        }
        return best;
    }

    private static PriceDisplayZone ResolveDisplayZone(
        decimal price,
        decimal? bestAsk,
        decimal? bestBid,
        int askVolume,
        int bidVolume)
    {
        if (askVolume > 0) return PriceDisplayZone.AskQuote;
        if (bidVolume > 0) return PriceDisplayZone.BidQuote;
        if (bestAsk is not null && price >= bestAsk.Value) return PriceDisplayZone.AskQuote;
        if (bestBid is not null && price <= bestBid.Value) return PriceDisplayZone.BidQuote;
        return PriceDisplayZone.Unquoted;
    }

    /// <summary>在 5 档报价中查找指定价位的挂单量（价位四舍五入到 tick）。</summary>
    private static int VolumeAt(decimal price, IReadOnlyList<decimal> prices, IReadOnlyList<int> volumes, decimal tick)
    {
        for (int i = 0; i < prices.Count && i < volumes.Count; i++)
        {
            if (Math.Abs(prices[i] - price) < tick / 2m)
                return volumes[i];
        }
        return 0;
    }

    /// <summary>把外部挂单聚合按价位（tick 对齐后）查找本档位挂单数；找不到返回 0。</summary>
    private static int LookupPending(IReadOnlyDictionary<decimal, int> pendingByPrice, decimal price, decimal tick)
    {
        // 聚合 key 由 TradingViewModel 写入时已经按 tick 对齐，但防御性扫描 ±tick/2 容差
        foreach (var kv in pendingByPrice)
        {
            if (Math.Abs(kv.Key - price) < tick / 2m)
                return kv.Value;
        }
        return 0;
    }
}
