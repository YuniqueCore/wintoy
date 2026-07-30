using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 交易服务工厂：按登录选择的连接端点创建 <see cref="ITradingService"/> 实例。
/// 与 <see cref="IMarketDataServiceFactory"/> 对称，登录后由 <c>SessionService</c> 调用。
/// </summary>
public interface ITradingServiceFactory
{
    /// <summary>创建交易服务（CTP 或 Mock，由实现决定）。</summary>
    ITradingService Create(ConnectionEndpoint endpoint);
}
