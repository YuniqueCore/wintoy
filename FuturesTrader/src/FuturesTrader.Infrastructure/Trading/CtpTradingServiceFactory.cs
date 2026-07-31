using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.Trading.Ctp;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// CTP 交易服务工厂：按 <see cref="ConnectionEndpoint"/> 创建 <see cref="CtpTradingService"/>。
/// 登录后由 SessionService 调用，按用户选择的账号交易地址 + 凭据重建。
/// </summary>
public sealed class CtpTradingServiceFactory : ITradingServiceFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly CtpApiRuntimeMode _apiRuntimeMode;

    public CtpTradingServiceFactory(
        ILoggerFactory loggerFactory,
        CtpApiRuntimeMode apiRuntimeMode = CtpApiRuntimeMode.Production)
    {
        _loggerFactory = loggerFactory;
        _apiRuntimeMode = apiRuntimeMode;
    }

    /// <inheritdoc />
    public ITradingService Create(ConnectionEndpoint endpoint)
    {
        var opts = new TradingOptions
        {
            Provider = TradingProvider.Ctp,
            FrontAddress = endpoint.FrontAddress,
            BrokerId = endpoint.BrokerId,
            UserId = endpoint.UserId,
            Password = endpoint.Password,
            AppId = endpoint.AppId,
            AuthCode = endpoint.AuthCode,
            FlowPath = string.IsNullOrEmpty(endpoint.FlowPath) ? "./TraderFlow/" : endpoint.FlowPath,
            UserProductInfo = endpoint.UserProductInfo,
            ApiRuntimeMode = _apiRuntimeMode
        };
        return new CtpTradingService(opts, _loggerFactory.CreateLogger<CtpTradingService>());
    }
}
