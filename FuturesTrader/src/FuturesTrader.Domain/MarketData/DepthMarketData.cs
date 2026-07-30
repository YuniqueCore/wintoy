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
    /// </summary>
    public PriceLadder ToPriceLadder(decimal priceTick, int levels)
    {
        if (priceTick <= 0) priceTick = 1m;
        if (levels <= 0) levels = 5;

        var rows = new List<PriceLevel>(levels * 2 + 1);
        // 上方卖盘（空单挂单区，红色）：价格从高到低，最接近 LastPrice 的在最下
        for (int i = levels; i >= 1; i--)
        {
            var price = LastPrice + i * priceTick;
            rows.Add(new PriceLevel
            {
                Price = price,
                AskVolume = VolumeAt(price, AskPrices, AskVolumes, priceTick),
                BidVolume = 0,
                Zone = PriceZone.Ask
            });
        }
        // 中心最新价行
        rows.Add(new PriceLevel
        {
            Price = LastPrice,
            IsLastPrice = true,
            AskVolume = VolumeAt(LastPrice, AskPrices, AskVolumes, priceTick),
            BidVolume = VolumeAt(LastPrice, BidPrices, BidVolumes, priceTick),
            Zone = PriceZone.Center
        });
        // 下方买盘（多单挂单区，蓝色）：价格从低到高，最接近 LastPrice 的在最上
        for (int i = 1; i <= levels; i++)
        {
            var price = LastPrice - i * priceTick;
            rows.Add(new PriceLevel
            {
                Price = price,
                BidVolume = VolumeAt(price, BidPrices, BidVolumes, priceTick),
                AskVolume = 0,
                Zone = PriceZone.Bid
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
}
