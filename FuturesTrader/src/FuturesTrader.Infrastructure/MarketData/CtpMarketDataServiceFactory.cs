using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.MarketData;

/// <summary>
/// CTP 行情服务工厂：按 <see cref="ConnectionEndpoint"/> 创建 <see cref="CtpMarketDataService"/>。
/// 替代旧版 DI Singleton 直读 appsettings 的模式——登录后由 SessionService 调用，按用户选择的上游地址重建。
/// </summary>
public sealed class CtpMarketDataServiceFactory : IMarketDataServiceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _priceLadderLevels;
    private readonly CtpApiRuntimeMode _apiRuntimeMode;

    /// <param name="loggerFactory">日志工厂，用于创建 <see cref="CtpMarketDataService"/> 的 logger。</param>
    /// <param name="priceLadderLevels">价差居中价格梯上下档位数（默认 5，对齐 CTP 5 档买卖盘）。</param>
    public CtpMarketDataServiceFactory(
        ILoggerFactory loggerFactory,
        int priceLadderLevels = 5,
        CtpApiRuntimeMode apiRuntimeMode = CtpApiRuntimeMode.Production)
    {
        _loggerFactory = loggerFactory;
        _priceLadderLevels = priceLadderLevels;
        _apiRuntimeMode = apiRuntimeMode;
    }

    /// <inheritdoc />
    public IMarketDataService Create(ConnectionEndpoint endpoint)
    {
        var opts = new MarketDataOptions
        {
            Provider = MarketDataProvider.Ctp,
            FrontAddress = endpoint.FrontAddress,
            BrokerId = endpoint.BrokerId,
            UserId = endpoint.UserId,
            Password = endpoint.Password,
            AppId = endpoint.AppId,
            AuthCode = endpoint.AuthCode,
            FlowPath = string.IsNullOrEmpty(endpoint.FlowPath) ? "./MdFlow/" : endpoint.FlowPath,
            PriceLadderLevels = _priceLadderLevels,
            ApiRuntimeMode = _apiRuntimeMode
        };
        return new CtpMarketDataService(opts, _loggerFactory.CreateLogger<CtpMarketDataService>());
    }
}
