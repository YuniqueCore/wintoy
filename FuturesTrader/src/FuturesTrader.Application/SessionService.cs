using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Application;

/// <summary>
/// 会话服务实现：登录流程编排（行情连接 → 交易连接 → 结算确认）。
/// <para>
/// 登录成功后持有 <see cref="MarketData"/> / <see cref="Trading"/> 实例，
/// 供 FloatingMainWindow / ContractWindow 使用。登出时 DisposeAsync 释放 CTP 连接。
/// </para>
/// <para>
/// 线程安全：<see cref="LoginAsync"/> / <see cref="LogoutAsync"/> 互斥（<c>SemaphoreSlim</c>），
/// 防止并发登录/登出导致状态错乱。状态变更通过 <see cref="StateChanged"/> 事件通知 UI。
/// </para>
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly IMarketDataServiceFactory _marketDataFactory;
    private readonly ITradingServiceFactory _tradingFactory;
    private readonly LoginOptions _loginOptions;
    private readonly ILogger<SessionService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private SessionState _state = new SessionState.Idle();
    private AccountEntry? _account;
    private IMarketDataService? _marketData;
    private ITradingService? _trading;

    public SessionService(
        IMarketDataServiceFactory marketDataFactory,
        ITradingServiceFactory tradingFactory,
        IOptions<LoginOptions> loginOptions,
        ILogger<SessionService> logger)
    {
        _marketDataFactory = marketDataFactory;
        _tradingFactory = tradingFactory;
        _loginOptions = loginOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public SessionState CurrentState => _state;

    /// <inheritdoc />
    public AccountEntry? Account => _account;

    /// <inheritdoc />
    public IMarketDataService? MarketData => _marketData;

    /// <inheritdoc />
    public ITradingService? Trading => _trading;

    /// <inheritdoc />
    public event EventHandler<SessionState>? StateChanged;

    /// <inheritdoc />
    public async Task<SessionState> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 幂等：已登录则先登出
            if (_state is SessionState.LoggedIn)
                await DisposeServicesAsync();

            Transition(new SessionState.LoggingIn());

            // 构造行情/交易连接端点
            var marketEndpoint = new ConnectionEndpoint
            {
                FrontAddress = request.HqAddress.Url,
                BrokerId = request.Account.BrokerId,
                UserId = request.Account.UserId,
                Password = request.Password,
                AppId = request.Account.AppId,
                AuthCode = request.Account.AuthCode,
                FlowPath = "./MdFlow/"
            };
            var tradingEndpoint = new ConnectionEndpoint
            {
                FrontAddress = request.Account.TradingAddress,
                BrokerId = request.Account.BrokerId,
                UserId = request.Account.UserId,
                Password = request.Password,
                AppId = request.Account.AppId,
                AuthCode = request.Account.AuthCode,
                FlowPath = "./TraderFlow/"
            };

            _logger.LogInformation("开始登录：账号 {UserId} 行情 {MdFront} 交易 {TdFront}",
                request.Account.UserId, marketEndpoint.FrontAddress, tradingEndpoint.FrontAddress);

            // 创建服务实例（工厂决定 CTP 或 Mock）
            _marketData = _marketDataFactory.Create(marketEndpoint);
            _trading = _tradingFactory.Create(tradingEndpoint);

            // 连接行情（带超时）
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(_loginOptions.ConnectTimeoutSec));

            try
            {
                await _marketData.ConnectAsync(connectCts.Token);
                _logger.LogInformation("行情连接成功");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "行情连接失败");
                Transition(new SessionState.Failed($"行情连接失败：{ex.Message}"));
                await DisposeServicesAsync();
                return _state;
            }

            // 连接交易（认证→登录→结算确认，CTP 实现内部完成）
            try
            {
                await _trading.ConnectAsync(connectCts.Token);
                _logger.LogInformation("交易连接成功（认证→登录→结算确认完成）");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "交易连接失败");
                Transition(new SessionState.Failed($"交易连接失败：{ex.Message}"));
                await DisposeServicesAsync();
                return _state;
            }

            _account = request.Account;
            Transition(new SessionState.LoggedIn());
            _logger.LogInformation("登录完成：账号 {UserId}", request.Account.UserId);
            return _state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登录异常");
            Transition(new SessionState.Failed($"登录异常：{ex.Message}"));
            await DisposeServicesAsync();
            return _state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state is not SessionState.LoggedIn)
                return;

            Transition(new SessionState.LoggingOut());
            await DisposeServicesAsync();
            _account = null;
            Transition(new SessionState.Idle());
            _logger.LogInformation("已登出");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>释放并清空行情/交易服务实例。</summary>
    private async Task DisposeServicesAsync()
    {
        if (_marketData is not null)
        {
            try { await _marketData.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "释放行情服务异常"); }
            _marketData = null;
        }
        if (_trading is not null)
        {
            try { await _trading.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "释放交易服务异常"); }
            _trading = null;
        }
    }

    /// <summary>切换状态并触发 <see cref="StateChanged"/> 事件。</summary>
    private void Transition(SessionState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }
}
