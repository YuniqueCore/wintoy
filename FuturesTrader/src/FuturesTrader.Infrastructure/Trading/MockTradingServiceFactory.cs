using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// 模拟交易服务工厂：创建 <see cref="MockTradingService"/>（离线开发/测试用）。
/// 忽略 <see cref="ConnectionEndpoint"/>。UI 会把报单保持为 Accepted，便于验证挂单数量和撤单。
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
        return new MockTradingService(
            _loggerFactory.CreateLogger<MockTradingService>(),
            autoFillDelay: null);
    }
}
