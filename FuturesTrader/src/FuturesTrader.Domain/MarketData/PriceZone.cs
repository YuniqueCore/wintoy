namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 价格梯行的行情显示状态。它只决定报价如何显示，绝不决定下单方向。
/// 顶部/底部有报价的行可以着色；中间没有报价文字的行保持中性显示，
/// 但第 1/3 交易列仍可被点击下单。
/// </summary>
public enum PriceDisplayZone
{
    /// <summary>卖方报价显示。</summary>
    AskQuote,

    /// <summary>无人报价的中间价格行。</summary>
    Unquoted,

    /// <summary>买方报价显示。</summary>
    BidQuote
}
