namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 行情数据源类型（appsettings MarketData:Provider 绑定）。
/// <para><see cref="Mock"/> = 模拟 tick（<c>SimulatedMarketDataService</c>），离线可验证；</para>
/// <para><see cref="Ctp"/> = 直连 CTP 6.7.10 <c>thostmduserapi_se.dll</c>。</para>
/// </summary>
public enum MarketDataProvider
{
    Mock,
    Ctp
}
