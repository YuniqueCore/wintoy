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
    /// 按价差居中语义生成价格梯：以 <see cref="LastPrice"/> 为中心、<paramref name="priceTick"/> 为步长，
    /// 上下各 <paramref name="levels"/> 档。中心行标注 <see cref="PriceLevel.IsLastPrice"/>。
    /// 5 档买卖盘按价位就近填充（中心上方卖盘、下方买盘）。
    /// <para>
    /// <paramref name="pendingByPrice"/> 可选：把外部维护的「价格 → 用户本地挂单数」聚合传入，
    /// 让 <see cref="PriceLevel.PendingOrderCount"/> 在 UI 第 0 列直接显示（点击可撤单）。
    /// 留空时所有 <see cref="PriceLevel.PendingOrderCount"/> = 0。
    /// </para>
    /// </summary>
    public PriceLadder ToPriceLadder(decimal priceTick, int levels, IReadOnlyDictionary<decimal, int>? pendingByPrice = null)
    {
        if (priceTick <= 0) priceTick = 1m;
        if (levels <= 0) levels = 5;
        pendingByPrice ??= new Dictionary<decimal, int>();

        var rows = new List<PriceLevel>(levels * 2 + 1);
        // 上方价格行：有卖方报价时以红色显示；没有报价时是中性的无人报价行。
        for (int i = levels; i >= 1; i--)
        {
            var price = LastPrice + i * priceTick;
            var askVolume = VolumeAt(price, AskPrices, AskVolumes, priceTick);
            rows.Add(new PriceLevel
            {
                Price = price,
                AskVolume = askVolume,
                BidVolume = 0,
                PendingOrderCount = LookupPending(pendingByPrice, price, priceTick),
                DisplayZone = askVolume > 0 ? PriceDisplayZone.AskQuote : PriceDisplayZone.Unquoted
            });
        }
        // 最新价行可能同时有报价，也可能位于无人报价中间区；最新价标记与颜色分开。
        var centerAskVolume = VolumeAt(LastPrice, AskPrices, AskVolumes, priceTick);
        var centerBidVolume = VolumeAt(LastPrice, BidPrices, BidVolumes, priceTick);
        rows.Add(new PriceLevel
        {
            Price = LastPrice,
            IsLastPrice = true,
            AskVolume = centerAskVolume,
            BidVolume = centerBidVolume,
            PendingOrderCount = LookupPending(pendingByPrice, LastPrice, priceTick),
            DisplayZone = centerAskVolume > 0
                ? PriceDisplayZone.AskQuote
                : centerBidVolume > 0
                    ? PriceDisplayZone.BidQuote
                    : PriceDisplayZone.Unquoted
        });
        // 下方价格行：有买方报价时以蓝色显示；没有报价时是中性的无人报价行。
        for (int i = 1; i <= levels; i++)
        {
            var price = LastPrice - i * priceTick;
            var bidVolume = VolumeAt(price, BidPrices, BidVolumes, priceTick);
            rows.Add(new PriceLevel
            {
                Price = price,
                BidVolume = bidVolume,
                AskVolume = 0,
                PendingOrderCount = LookupPending(pendingByPrice, price, priceTick),
                DisplayZone = bidVolume > 0 ? PriceDisplayZone.BidQuote : PriceDisplayZone.Unquoted
            });
        }
        return new PriceLadder(levels, LastPrice, priceTick, rows);
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
