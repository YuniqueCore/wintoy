namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 投机套保标志（CTP <c>TThostFtdcHedgeFlagType</c>）。
/// <para>Speculation='1' 投机（默认）；Arbitrage='2' 套利；Hedge='3' 套保。</para>
/// 与 <see cref="OffsetFlag"/>（开平标志）正交：HedgeFlag 描述持仓性质，OffsetFlag 描述开平动作。
/// 强类型替代裸 char，避免与开平标志混用。
/// </summary>
public enum HedgeFlag
{
    /// <summary>投机（CTP '1'，默认）。</summary>
    Speculation = 1,

    /// <summary>套利（CTP '2'）。</summary>
    Arbitrage = 2,

    /// <summary>套保（CTP '3'）。</summary>
    Hedge = 3
}
