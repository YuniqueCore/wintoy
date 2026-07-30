using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// 模拟交易服务工厂：创建 <see cref="MockTradingService"/>（离线开发/测试用）。
/// 忽略 <see cref="ConnectionEndpoint"/>，仅用本地模拟报单/撤单/成交。
/// </summary>
public sealed class MockTradingServiceFactory : ITradingServiceFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public MockTradingServiceFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public ITradingService Create(ConnectionEndpoint endpoint)
    {
        return new MockTradingService(_loggerFactory.CreateLogger<MockTradingService>());
    }
}
