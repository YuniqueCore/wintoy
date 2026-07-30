using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 行情服务工厂：按登录选择的连接端点创建 <see cref="IMarketDataService"/> 实例。
/// 替代旧版 DI Singleton 直读 appsettings 的模式——登录后由 <c>SessionService</c> 调用工厂按用户选择重建。
/// </summary>
public interface IMarketDataServiceFactory
{
    /// <summary>创建行情服务（CTP 或 Mock，由实现决定）。</summary>
    IMarketDataService Create(ConnectionEndpoint endpoint);
}
