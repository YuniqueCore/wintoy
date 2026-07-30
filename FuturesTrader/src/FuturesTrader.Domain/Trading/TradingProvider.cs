namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 交易服务实现类型（appsettings Trading:Provider 绑定）。
/// <para><see cref="Mock"/> = 本地模拟（<c>MockTradingService</c>），离线可验证下单/撤单/风控链路；</para>
/// <para><see cref="Ctp"/> = 直连 CTP 6.7.10 <c>thosttraderapi_se.dll</c>（需认证 BrokerID/UserID/Password/AppID/AuthCode）。</para>
/// </summary>
public enum TradingProvider
{
    Mock,
    Ctp
}
