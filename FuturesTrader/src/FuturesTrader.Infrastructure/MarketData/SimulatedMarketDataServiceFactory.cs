using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.MarketData;

/// <summary>
/// 模拟行情服务工厂：创建 <see cref="SimulatedMarketDataService"/>（离线开发/测试用）。
/// 忽略 <see cref="ConnectionEndpoint"/>，仅用模拟随机游走数据。
/// </summary>
public sealed class SimulatedMarketDataServiceFactory : IMarketDataServiceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _mockTickIntervalMs;

    public SimulatedMarketDataServiceFactory(ILoggerFactory loggerFactory, int mockTickIntervalMs = 500)
    {
        _loggerFactory = loggerFactory;
        _mockTickIntervalMs = mockTickIntervalMs;
    }

    /// <inheritdoc />
    public IMarketDataService Create(ConnectionEndpoint endpoint)
    {
        return new SimulatedMarketDataService(
            _mockTickIntervalMs,
            _loggerFactory.CreateLogger<SimulatedMarketDataService>());
    }
}
