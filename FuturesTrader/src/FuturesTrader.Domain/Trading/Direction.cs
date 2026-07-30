namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 买卖方向（CTP <c>TThostFtdcDirectionType</c>）。
/// <para><see cref="Buy"/> = '0' 买（多开/空平）；<see cref="Sell"/> = '1' 卖（空开/多平）。</para>
/// 强类型替代裸 char，避免方向与开平标志混用。
/// </summary>
public enum Direction
{
    /// <summary>买（CTP '0'）。</summary>
    Buy = 0,

    /// <summary>卖（CTP '1'）。</summary>
    Sell = 1
}
