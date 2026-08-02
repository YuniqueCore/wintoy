namespace FuturesTrader.Domain.Configuration;

/// <summary>
/// 对应 config.ini [Window] 段：窗口外观与交互参数。
/// </summary>
public sealed record WindowConfig
{
    /// <summary>主字体（GBK 编码，如"新宋体"）</summary>
    public string MainFont { get; init; } = "新宋体";

    /// <summary>窗口排列紧凑度</summary>
    public int CompactSpacing { get; init; } = 7;

    /// <summary>字号偏移</summary>
    public int FontSizeOffset { get; init; }

    /// <summary>价格列表边距</summary>
    public int PriceListMargin { get; init; } = 5;

    /// <summary>标题栏减少高度</summary>
    public int DecTitle { get; init; } = 30;

    /// <summary>对齐方式</summary>
    public int Align { get; init; } = 1;

    /// <summary>窄模式减少宽度</summary>
    public int NarrowReduceLength { get; init; } = 40;

    /// <summary>行情滚轮加速倍数</summary>
    public int MouseWheelSpeed { get; init; } = 3;

    /// <summary>窗口大小自动调整开关</summary>
    public bool AutoSize { get; init; }

    /// <summary>所有合约窗口共用的价格梯单行高度。</summary>
    public int TickRowHeights { get; init; } = 12;

    /// <summary>所有合约窗口共用的卖一向上空区行数。</summary>
    public int AskQuoteRowCount { get; init; } = 30;

    /// <summary>所有合约窗口共用的买一向下多区行数。</summary>
    public int BidQuoteRowCount { get; init; } = 30;

    /// <summary>合约窗口高度</summary>
    public int InstrumentWindowHeights { get; init; } = 1000;

    /// <summary>价格列宽比例（5 列：买量/买价/最新/卖价/卖量）</summary>
    public IReadOnlyList<int> PriceListRatios { get; init; } = [10, 25, 30, 25, 10];
}
