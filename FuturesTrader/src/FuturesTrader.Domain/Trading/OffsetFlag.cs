namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 开平标志（CTP <c>TThostFtdcOffsetFlagType</c>）。
/// <para>Open='0' 开仓；Close='1' 平仓；CloseToday='3' 平今；CloseYesterday='4' 平昨。</para>
/// 上期所区分平今/平昨（SHFE 强制要求 CloseToday 平今仓），
/// 其他交易所用 Close 即可自动匹配。ForceClose='2' 仅风控触发时使用，本系统不暴露。
/// </summary>
public enum OffsetFlag
{
    /// <summary>开仓（CTP '0'）。</summary>
    Open = 0,

    /// <summary>平仓（CTP '1'，通用平仓，非 SHFE 用此）。</summary>
    Close = 1,

    /// <summary>平今（CTP '3'，SHFE 专用）。</summary>
    CloseToday = 3,

    /// <summary>平昨（CTP '4'，SHFE 专用）。</summary>
    CloseYesterday = 4
}
