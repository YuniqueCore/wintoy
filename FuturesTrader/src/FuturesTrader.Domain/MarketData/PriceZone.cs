namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 价格梯区域：以最新价中心行划分，上方为空单挂单区（红），下方为多单挂单区（蓝），中心为高亮行。
/// 对齐 0527.exe 点价窗口 PriceList 红蓝背景分区。
/// </summary>
public enum PriceZone
{
    /// <summary>空单挂单区（中心上方，红色背景）——左键挂空单。</summary>
    Ask,

    /// <summary>中心最新价行（高亮）。</summary>
    Center,

    /// <summary>多单挂单区（中心下方，蓝色背景）——左键挂多单。</summary>
    Bid
}
