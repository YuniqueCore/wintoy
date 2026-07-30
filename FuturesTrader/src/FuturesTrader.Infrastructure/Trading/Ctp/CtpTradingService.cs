using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;
using FuturesTrader.Infrastructure.Trading.Ctp.Native;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading.Ctp;

/// <summary>
/// <see cref="ITradingService"/> 的 CTP 实现：直连 <c>thosttraderapi_se.dll</c> 6.7.13。
/// 完整链路：CreateFtdcTraderApi → RegisterSpi → SubscribePrivateTopic/PublicTopic → RegisterFront → Init →
/// OnFrontConnected → ReqAuthenticate(BrokerID/UserID/AppID/AuthCode) → OnRspAuthenticate →
/// ReqUserLogin(BrokerID/UserID/Password) → OnRspUserLogin → ReqSettlementInfoConfirm →
/// OnRspSettlementInfoConfirm → Connected → ReqOrderInsert/ReqOrderAction。
/// <para>
/// <b>认证流程</b>（CTP 6.5+ 强制）：认证 → 登录 → 结算确认三步，任一步失败即 <see cref="ConnectionState.Failed"/>。
/// 结算确认成功后才标记 Connected，ConnectAsync 返回。
/// </para>
/// <para>
/// <b>报单/撤单</b>：<see cref="SendOrderAsync"/> 调 ReqOrderInsert 后立即返回 OrderRef，
/// 实际结果通过 <see cref="OrderStream"/>（OnRtnOrder）和 <see cref="TradeStream"/>（OnRtnTrade）异步推送。
/// </para>
/// </summary>
public sealed class CtpTradingService : ITradingService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly TradingOptions _options;
    private readonly ILogger<CtpTradingService> _logger;
    private readonly Subject<OrderResult> _orders = new();
    private readonly Subject<Trade> _trades = new();
    private readonly Subject<Position> _positions = new();
    private readonly Subject<Instrument> _instruments = new();
    private readonly Subject<TradingAccount> _accounts = new();
    private readonly Subject<ConnectionState> _connection = new();
    private readonly object _apiLock = new();

    private CtpTraderSpiBridge? _spi;
    private IntPtr _apiPtr = IntPtr.Zero;
    private int _requestIdSeq;
    private int _orderRefSeq;
    private int _frontId;
    private int _sessionId;
    private int _disposed;
    private TaskCompletionSource<bool>? _connectTcs;
    private Task? _connectTask;

    public CtpTradingService(TradingOptions options, ILogger<CtpTradingService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CtpTradingService>.Instance;

        if (string.IsNullOrWhiteSpace(_options.FrontAddress))
            throw new ArgumentException("Trading:FrontAddress 未配置（Ctp 模式必填）", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.FlowPath))
            throw new ArgumentException("Trading:FlowPath 未配置", nameof(options));
    }

    /// <inheritdoc />
    public ConnectionState CurrentState { get; private set; } = new ConnectionState.Disconnected();

    /// <inheritdoc />
    public IObservable<OrderResult> OrderStream => _orders;

    /// <inheritdoc />
    public IObservable<Trade> TradeStream => _trades;

    /// <inheritdoc />
    public IObservable<Position> PositionStream => _positions;

    /// <inheritdoc />
    public IObservable<Instrument> InstrumentStream => _instruments;

    /// <inheritdoc />
    public IObservable<TradingAccount> AccountStream => _accounts;

    /// <inheritdoc />
    public IObservable<ConnectionState> ConnectionStream => _connection;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_apiLock)
        {
            if (_connectTask is { IsCompleted: false } pending) return pending;
            if (_apiPtr != IntPtr.Zero && CurrentState is ConnectionState.Connected)
            {
                _logger.LogDebug("CtpTrading 已连接，跳过重复 Connect");
                return Task.CompletedTask;
            }

            TransitionTo(new ConnectionState.Connecting());
            EnsureFlowPath();

            _spi = new CtpTraderSpiBridge();
            _spi.FrontConnected += OnFrontConnected;
            _spi.FrontDisconnected += OnFrontDisconnected;
            _spi.RspAuthenticate += OnRspAuthenticate;
            _spi.RspUserLogin += OnRspUserLogin;
            _spi.RspSettlementInfoConfirm += OnRspSettlementInfoConfirm;
            _spi.RspOrderInsert += OnRspOrderInsert;
            _spi.RspOrderAction += OnRspOrderAction;
            _spi.RspError += OnRspError;
            _spi.RtnOrder += OnRtnOrder;
            _spi.RtnTrade += OnRtnTrade;
            _spi.RspQryInvestorPosition += OnRspQryInvestorPosition;
            _spi.RspQryTradingAccount += OnRspQryTradingAccount;
            _spi.RspQryInstrument += OnRspQryInstrument;

            _apiPtr = ThostTraderApiNative.CreateFtdcTraderApi(_options.FlowPath);
            if (_apiPtr == IntPtr.Zero)
            {
                TransitionTo(new ConnectionState.Failed("CreateFtdcTraderApi 返回 null"));
                _spi.Dispose();
                _spi = null;
                throw new InvalidOperationException("CreateFtdcTraderApi 失败，请检查 thosttraderapi_se.dll 是否在 PATH/输出目录");
            }

            _connectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // RegisterSpi → SubscribePrivateTopic(RESUME) → SubscribePublicTopic(RESUME) → RegisterFront → Init
            ThostTraderApiNative.RegisterSpi(_apiPtr, _spi.SpiPointer);
            ThostTraderApiNative.SubscribePrivateTopic(_apiPtr, ThostTraderApiNative.TertResume);
            ThostTraderApiNative.SubscribePublicTopic(_apiPtr, ThostTraderApiNative.TertResume);
            ThostTraderApiNative.RegisterFront(_apiPtr, _options.FrontAddress);
            _logger.LogInformation("CtpTrading Init 中：Front={Front} Flow={Flow}",
                _options.FrontAddress, _options.FlowPath);
            ThostTraderApiNative.Init(_apiPtr);

            _connectTask = WaitForConnectAsync(cancellationToken);
            return _connectTask;
        }
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero) return Task.CompletedTask;
            try { ThostTraderApiNative.RegisterSpi(_apiPtr, IntPtr.Zero); }
            catch (Exception ex) { _logger.LogWarning(ex, "RegisterSpi(null) 异常（忽略）"); }
            try { ThostTraderApiNative.Release(_apiPtr); }
            catch (Exception ex) { _logger.LogWarning(ex, "Release 异常（忽略）"); }

            _apiPtr = IntPtr.Zero;
            _spi?.Dispose();
            _spi = null;
            _connectTcs?.TrySetCanceled();
            _connectTcs = null;
            _connectTask = null;
        }
        TransitionTo(new ConnectionState.Disconnected());
        _logger.LogInformation("CtpTrading 已断开");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> SendOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(request);

        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法下单");

        if (request.Volume <= 0)
            throw new ArgumentException("报单数量必须 > 0", nameof(request));

        string orderRef = request.OrderRef ?? NextOrderRef();
        int reqId = Interlocked.Increment(ref _requestIdSeq);

        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
                throw new InvalidOperationException("交易 API 已释放");

            IntPtr pOrder = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcInputOrderField>());
            try
            {
                var field = BuildInputOrder(request, orderRef, reqId);
                Marshal.StructureToPtr(field, pOrder, fDeleteOld: false);
                int ret = ThostTraderApiNative.ReqOrderInsert(_apiPtr, pOrder, reqId);
                if (ret != 0)
                    throw new InvalidOperationException($"ReqOrderInsert 返回 {ret}（-1=网络失败 -2=未处理请求超限 -3=流控）");
            }
            finally
            {
                Marshal.DestroyStructure<CThostFtdcInputOrderField>(pOrder);
                Marshal.FreeHGlobal(pOrder);
            }
        }

        _logger.LogInformation("报单已提交：{Instrument} {Direction} {Offset} {Volume}@{Price} Ref={Ref}",
            request.InstrumentId, request.Direction, request.OffsetFlag, request.Volume, request.Price, orderRef);
        return Task.FromResult(orderRef);
    }

    /// <inheritdoc />
    public Task CancelOrderAsync(string orderRef, int frontId, int sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(orderRef);

        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法撤单");

        int reqId = Interlocked.Increment(ref _requestIdSeq);
        int actionRef = Interlocked.Increment(ref _orderRefSeq);

        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
                throw new InvalidOperationException("交易 API 已释放");

            IntPtr pAction = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcInputOrderActionField>());
            try
            {
                var field = new CThostFtdcInputOrderActionField
                {
                    BrokerID = _options.BrokerId,
                    InvestorID = _options.UserId,
                    OrderActionRef = actionRef,
                    OrderRef = orderRef,
                    FrontID = frontId,
                    SessionID = sessionId,
                    ActionFlag = (byte)'0', // THOST_FTDC_AF_Delete
                    UserID = _options.UserId
                };
                Marshal.StructureToPtr(field, pAction, fDeleteOld: false);
                int ret = ThostTraderApiNative.ReqOrderAction(_apiPtr, pAction, reqId);
                if (ret != 0)
                    throw new InvalidOperationException($"ReqOrderAction 返回 {ret}");
            }
            finally
            {
                Marshal.DestroyStructure<CThostFtdcInputOrderActionField>(pAction);
                Marshal.FreeHGlobal(pAction);
            }
        }

        _logger.LogInformation("撤单已提交：Ref={Ref} Front={Front} Session={Session}", orderRef, frontId, sessionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryPositionAsync(string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询持仓");
        int reqId = Interlocked.Increment(ref _requestIdSeq);
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
                throw new InvalidOperationException("交易 API 已释放");
            IntPtr pQry = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcQryInvestorPositionField>());
            try
            {
                var field = new CThostFtdcQryInvestorPositionField
                {
                    BrokerID = _options.BrokerId,
                    InvestorID = _options.UserId,
                    InstrumentID = instrumentId ?? string.Empty
                };
                Marshal.StructureToPtr(field, pQry, fDeleteOld: false);
                int ret = ThostTraderApiNative.ReqQryInvestorPosition(_apiPtr, pQry, reqId);
                if (ret != 0)
                    _logger.LogWarning("ReqQryInvestorPosition 返回 {Ret}（-3=流控，需间隔 ≥1s 重试）", ret);
            }
            finally
            {
                Marshal.DestroyStructure<CThostFtdcQryInvestorPositionField>(pQry);
                Marshal.FreeHGlobal(pQry);
            }
        }
        _logger.LogDebug("查询持仓：Instrument={Inst} ReqId={ReqId}", instrumentId ?? "(全量)", reqId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryInstrumentAsync(string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询合约");
        int reqId = Interlocked.Increment(ref _requestIdSeq);
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
                throw new InvalidOperationException("交易 API 已释放");
            IntPtr pQry = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcQryInstrumentField>());
            try
            {
                var field = new CThostFtdcQryInstrumentField
                {
                    InstrumentID = instrumentId ?? string.Empty
                };
                Marshal.StructureToPtr(field, pQry, fDeleteOld: false);
                int ret = ThostTraderApiNative.ReqQryInstrument(_apiPtr, pQry, reqId);
                if (ret != 0)
                    _logger.LogWarning("ReqQryInstrument 返回 {Ret}（-3=流控）", ret);
            }
            finally
            {
                Marshal.DestroyStructure<CThostFtdcQryInstrumentField>(pQry);
                Marshal.FreeHGlobal(pQry);
            }
        }
        _logger.LogDebug("查询合约：Instrument={Inst} ReqId={ReqId}", instrumentId ?? "(全量)", reqId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryTradingAccountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询资金");
        int reqId = Interlocked.Increment(ref _requestIdSeq);
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
                throw new InvalidOperationException("交易 API 已释放");
            IntPtr pQry = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcQryTradingAccountField>());
            try
            {
                var field = new CThostFtdcQryTradingAccountField
                {
                    BrokerID = _options.BrokerId,
                    InvestorID = _options.UserId
                };
                Marshal.StructureToPtr(field, pQry, fDeleteOld: false);
                int ret = ThostTraderApiNative.ReqQryTradingAccount(_apiPtr, pQry, reqId);
                if (ret != 0)
                    _logger.LogWarning("ReqQryTradingAccount 返回 {Ret}（-3=流控）", ret);
            }
            finally
            {
                Marshal.DestroyStructure<CThostFtdcQryTradingAccountField>(pQry);
                Marshal.FreeHGlobal(pQry);
            }
        }
        _logger.LogDebug("查询资金账户 ReqId={ReqId}", reqId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed == 1) return;
        // 先断开（_disposed 仍为 0，ThrowIfDisposed 不触发），后标记已释放（原子操作防重入）
        try { await DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Dispose 时 Disconnect 异常"); }
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _orders.OnCompleted();
        _trades.OnCompleted();
        _connection.OnCompleted();
    }

    // ===== 回调处理（CTP 工作线程） =====

    private void OnFrontConnected()
    {
        _logger.LogInformation("CtpTrading 前端已连接，发起 ReqAuthenticate");
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return;
                int reqId = Interlocked.Increment(ref _requestIdSeq);
                IntPtr pAuth = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcReqAuthenticateField>());
                try
                {
                    var field = new CThostFtdcReqAuthenticateField
                    {
                        BrokerID = _options.BrokerId,
                        UserID = _options.UserId,
                        UserProductInfo = _options.UserProductInfo,
                        AuthCode = _options.AuthCode,
                        AppID = _options.AppId
                    };
                    Marshal.StructureToPtr(field, pAuth, fDeleteOld: false);
                    int ret = ThostTraderApiNative.ReqAuthenticate(_apiPtr, pAuth, reqId);
                    if (ret != 0)
                    {
                        _connectTcs?.TrySetException(new InvalidOperationException($"ReqAuthenticate 返回 {ret}"));
                        TransitionTo(new ConnectionState.Failed($"ReqAuthenticate 返回 {ret}"));
                    }
                }
                finally
                {
                    Marshal.DestroyStructure<CThostFtdcReqAuthenticateField>(pAuth);
                    Marshal.FreeHGlobal(pAuth);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnFrontConnected 处理异常");
            _connectTcs?.TrySetException(ex);
        }
    }

    private void OnFrontDisconnected(int nReason)
    {
        _logger.LogWarning("CtpTrading 前端断开 Reason=0x{Reason:X}", nReason);
        TransitionTo(new ConnectionState.Reconnecting(1, TimeSpan.FromSeconds(5)));
    }

    private void OnRspAuthenticate(bool success, string error)
    {
        if (!success)
        {
            _logger.LogError("CtpTrading 认证失败: {Error}", error);
            TransitionTo(new ConnectionState.Failed($"认证失败: {error}"));
            _connectTcs?.TrySetException(new InvalidOperationException($"认证失败: {error}"));
            return;
        }
        _logger.LogInformation("CtpTrading 认证成功，发起 ReqUserLogin");
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return;
                int reqId = Interlocked.Increment(ref _requestIdSeq);
                IntPtr pLogin = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcReqUserLoginField>());
                try
                {
                    var field = new CThostFtdcReqUserLoginField
                    {
                        BrokerID = _options.BrokerId,
                        UserID = _options.UserId,
                        Password = _options.Password,
                        UserProductInfo = _options.UserProductInfo
                    };
                    Marshal.StructureToPtr(field, pLogin, fDeleteOld: false);
                    int ret = ThostTraderApiNative.ReqUserLogin(_apiPtr, pLogin, reqId);
                    if (ret != 0)
                    {
                        _connectTcs?.TrySetException(new InvalidOperationException($"ReqUserLogin 返回 {ret}"));
                        TransitionTo(new ConnectionState.Failed($"ReqUserLogin 返回 {ret}"));
                    }
                }
                finally
                {
                    Marshal.DestroyStructure<CThostFtdcReqUserLoginField>(pLogin);
                    Marshal.FreeHGlobal(pLogin);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRspAuthenticate 处理异常");
            _connectTcs?.TrySetException(ex);
        }
    }

    private void OnRspUserLogin(bool success, string error)
    {
        if (!success)
        {
            _logger.LogError("CtpTrading 登录失败: {Error}", error);
            TransitionTo(new ConnectionState.Failed($"登录失败: {error}"));
            _connectTcs?.TrySetException(new InvalidOperationException($"登录失败: {error}"));
            return;
        }
        _logger.LogInformation("CtpTrading 登录成功，交易日={Day}", TryGetTradingDay());
        // 记录 FrontID/SessionID（从 RspUserLogin 读取，但这里简化：等 OnRtnOrder 回报中获取）
        // 实际上 FrontID/SessionID 在 RspUserLoginField 中，但我们的 bridge 只传 success/error
        // 暂存会在 OnRtnOrder 时从 OrderField 获取，用于撤单
        _logger.LogInformation("CtpTrading 发起 ReqSettlementInfoConfirm");
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return;
                int reqId = Interlocked.Increment(ref _requestIdSeq);
                IntPtr pConfirm = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcSettlementInfoConfirmField>());
                try
                {
                    var field = new CThostFtdcSettlementInfoConfirmField
                    {
                        BrokerID = _options.BrokerId,
                        InvestorID = _options.UserId
                    };
                    Marshal.StructureToPtr(field, pConfirm, fDeleteOld: false);
                    int ret = ThostTraderApiNative.ReqSettlementInfoConfirm(_apiPtr, pConfirm, reqId);
                    if (ret != 0)
                    {
                        _connectTcs?.TrySetException(new InvalidOperationException($"ReqSettlementInfoConfirm 返回 {ret}"));
                        TransitionTo(new ConnectionState.Failed($"ReqSettlementInfoConfirm 返回 {ret}"));
                    }
                }
                finally
                {
                    Marshal.DestroyStructure<CThostFtdcSettlementInfoConfirmField>(pConfirm);
                    Marshal.FreeHGlobal(pConfirm);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRspUserLogin 处理异常");
            _connectTcs?.TrySetException(ex);
        }
    }

    private void OnRspSettlementInfoConfirm(bool success, string error)
    {
        if (!success)
        {
            _logger.LogError("CtpTrading 结算确认失败: {Error}", error);
            TransitionTo(new ConnectionState.Failed($"结算确认失败: {error}"));
            _connectTcs?.TrySetException(new InvalidOperationException($"结算确认失败: {error}"));
            return;
        }
        _logger.LogInformation("CtpTrading 结算确认成功，交易就绪");
        TransitionTo(new ConnectionState.Connected());
        _connectTcs?.TrySetResult(true);
        // 连接就绪后自动查询持仓 + 资金（浮动栏初始数据源）
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false); // CTP 流控间隔
                await QueryPositionAsync(null, CancellationToken.None).ConfigureAwait(false);
                await Task.Delay(1000, CancellationToken.None).ConfigureAwait(false); // 查询间隔 ≥ 1s
                await QueryTradingAccountAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "连接后自动查询持仓/资金失败（非致命）");
            }
        });
    }

    private void OnRspOrderInsert(bool success, string error, int nRequestID)
    {
        if (!success)
        {
            _logger.LogError("CtpTrading 报单录入被拒: {Error} ReqId={ReqId}", error, nRequestID);
            // 报单被拒，推送 Rejected 状态（OrderRef 未知，但 CTP 会在 OnRtnOrder 中补推送）
            // OnRspOrderInsert 是同步拒绝，OnRtnOrder 会异步推送完整信息
        }
    }

    private void OnRspOrderAction(bool success, string error, int nRequestID)
    {
        if (!success)
            _logger.LogError("CtpTrading 撤单被拒: {Error} ReqId={ReqId}", error, nRequestID);
    }

    private void OnRspError(int errorId, string errorMsg, int nRequestId)
    {
        _logger.LogError("CtpTrading OnRspError: ErrorId={Id} Msg={Msg} ReqId={ReqId}", errorId, errorMsg, nRequestId);
    }

    private void OnRtnOrder(IntPtr pOrder)
    {
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcOrderField>(pOrder);
            // 记录 FrontID/SessionID（撤单需要）
            if (_frontId == 0 && field.FrontID != 0)
            {
                _frontId = field.FrontID;
                _sessionId = field.SessionID;
            }
            var result = MapToDomain(field);
            _orders.OnNext(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRtnOrder 映射失败");
        }
    }

    private void OnRtnTrade(IntPtr pTrade)
    {
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcTradeField>(pTrade);
            var trade = MapToDomain(field);
            _trades.OnNext(trade);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRtnTrade 映射失败");
        }
    }

    private void OnRspQryInvestorPosition(IntPtr pField, bool bIsLast, int nRequestID)
    {
        if (pField == IntPtr.Zero) return;
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcInvestorPositionField>(pField);
            _positions.OnNext(MapToDomain(field));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRspQryInvestorPosition 映射失败");
        }
    }

    private void OnRspQryTradingAccount(IntPtr pField, bool bIsLast, int nRequestID)
    {
        if (pField == IntPtr.Zero) return;
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcTradingAccountField>(pField);
            _accounts.OnNext(MapToDomain(field));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRspQryTradingAccount 映射失败");
        }
    }

    private void OnRspQryInstrument(IntPtr pField, bool bIsLast, int nRequestID)
    {
        if (pField == IntPtr.Zero) return;
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcInstrumentField>(pField);
            _instruments.OnNext(MapToDomain(field));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRspQryInstrument 映射失败");
        }
    }

    // ===== 内部辅助 =====

    private async Task WaitForConnectAsync(CancellationToken cancellationToken)
    {
        var tcs = _connectTcs;
        if (tcs == null) return;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ConnectTimeout);
            using var registration = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));
            await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TransitionTo(new ConnectionState.Failed($"CtpTrading 连接超时（{ConnectTimeout.TotalSeconds:F0}s）"));
            throw new TimeoutException($"CtpTrading 连接超时（{ConnectTimeout.TotalSeconds:F0}s），请检查 FrontAddress/认证/登录配置");
        }
    }

    /// <summary>构造 CThostFtdcInputOrderField（限价单 + GFD + 立即触发 + 投机套保 + 非强平）。</summary>
    private CThostFtdcInputOrderField BuildInputOrder(OrderRequest request, string orderRef, int reqId)
    {
        return new CThostFtdcInputOrderField
        {
            BrokerID = _options.BrokerId,
            InvestorID = _options.UserId,
            InstrumentID = request.InstrumentId,
            OrderRef = orderRef,
            UserID = _options.UserId,
            OrderPriceType = (byte)'2',        // THOST_FTDC_OPT_LimitPrice 限价
            Direction = (byte)(request.Direction == Direction.Buy ? '0' : '1'),
            CombOffsetFlag = MapOffsetFlag(request.OffsetFlag),
            CombHedgeFlag = "1\0\0\0\0",        // THOST_FTDC_HF_Speculation 投机
            LimitPrice = (double)request.Price,
            VolumeTotalOriginal = request.Volume,
            TimeCondition = (byte)'3',          // THOST_FTDC_TC_GFD 当日有效
            VolumeCondition = (byte)'1',        // THOST_FTDC_VC_AV 任意数量
            MinVolume = 1,
            ContingentCondition = (byte)'1',    // THOST_FTDC_CC_Immediately 立即
            StopPrice = 0,
            ForceCloseReason = (byte)'0',       // THOST_FTDC_FCC_NotForceClose 非强平
            IsAutoSuspend = 0,
            RequestID = reqId,
            UserForceClose = 0,
            IsSwapOrder = 0
        };
    }

    /// <summary>Domain OffsetFlag → CTP CombOffsetFlag[0] 字符。</summary>
    private static string MapOffsetFlag(OffsetFlag flag) => flag switch
    {
        OffsetFlag.Open => "0\0\0\0\0",
        OffsetFlag.Close => "1\0\0\0\0",
        OffsetFlag.CloseToday => "3\0\0\0\0",
        OffsetFlag.CloseYesterday => "4\0\0\0\0",
        _ => "0\0\0\0\0"
    };

    /// <summary>CTP OrderField → Domain OrderResult（含状态机映射）。</summary>
    private OrderResult MapToDomain(in CThostFtdcOrderField f)
    {
        return new OrderResult
        {
            OrderRef = f.OrderRef ?? string.Empty,
            FrontId = f.FrontID,
            SessionId = f.SessionID,
            ExchangeId = f.OrderSysID ?? string.Empty,
            InstrumentId = f.InstrumentID ?? string.Empty,
            Direction = f.Direction == (byte)'0' ? Direction.Buy : Direction.Sell,
            OffsetFlag = MapOffsetFlag(f.CombOffsetFlag),
            Price = (decimal)f.LimitPrice,
            Volume = f.VolumeTotalOriginal,
            VolumeTraded = f.VolumeTraded,
            VolumeRemaining = f.VolumeTotal,
            Status = MapOrderStatus(f.OrderStatus, f.VolumeTraded),
            InsertTime = ParseTime(f.InsertTime),
            StatusMessage = f.StatusMsg ?? string.Empty
        };
    }

    private static OffsetFlag MapOffsetFlag(string combOffsetFlag)
    {
        if (string.IsNullOrEmpty(combOffsetFlag)) return OffsetFlag.Open;
        return combOffsetFlag[0] switch
        {
            '0' => OffsetFlag.Open,
            '1' => OffsetFlag.Close,
            '3' => OffsetFlag.CloseToday,
            '4' => OffsetFlag.CloseYesterday,
            _ => OffsetFlag.Open
        };
    }

    private static OrderStatus MapOrderStatus(byte ctpStatus, int volumeTraded)
    {
        return (char)ctpStatus switch
        {
            '0' => new OrderStatus.Filled(volumeTraded),                    // AllTraded
            '1' => new OrderStatus.PartiallyFilled(volumeTraded),           // PartTradedQueueing
            '2' => new OrderStatus.Canceled(volumeTraded),                  // PartTradedNotQueueing
            '3' => new OrderStatus.Accepted(),                              // NoTradeQueueing
            '4' => new OrderStatus.Canceled(volumeTraded),                  // NoTradeNotQueueing
            '5' => new OrderStatus.Canceled(volumeTraded),                  // Canceled
            'a' => new OrderStatus.Pending(),                               // Unknown
            'b' => new OrderStatus.Accepted(),                              // NotTouched
            'c' => new OrderStatus.Accepted(),                              // Touched
            _ => new OrderStatus.Pending()
        };
    }

    /// <summary>CTP TradeField → Domain Trade。</summary>
    private static Trade MapToDomain(in CThostFtdcTradeField f)
    {
        return new Trade
        {
            TradeId = f.TradeID ?? string.Empty,
            OrderRef = f.OrderRef ?? string.Empty,
            InstrumentId = f.InstrumentID ?? string.Empty,
            Direction = f.Direction == (byte)'0' ? Direction.Buy : Direction.Sell,
            OffsetFlag = f.OffsetFlag switch
            {
                (byte)'0' => OffsetFlag.Open,
                (byte)'1' => OffsetFlag.Close,
                (byte)'3' => OffsetFlag.CloseToday,
                (byte)'4' => OffsetFlag.CloseYesterday,
                _ => OffsetFlag.Open
            },
            Price = (decimal)f.Price,
            Volume = f.Volume,
            TradeTime = ParseTime(f.TradeTime),
            TradingDay = f.TradingDay ?? string.Empty,
            ExchangeId = f.ExchangeID ?? string.Empty
        };
    }

    private static TimeOnly ParseTime(string s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        return TimeOnly.TryParse(s, out var t) ? t : default;
    }

    /// <summary>
    /// CTP InvestorPositionField → Domain Position。
    /// CTP 按 (合约,方向,投机套保) 分组推送多条；Domain 层按方向聚合。
    /// <see cref="Position.VolumeMultiple"/> 不在持仓结构内，消费者从 <see cref="InstrumentStream"/> 单独获取。
    /// </summary>
    private static Position MapToDomain(in CThostFtdcInvestorPositionField f)
    {
        return new Position
        {
            InstrumentId = f.InstrumentID ?? string.Empty,
            InvestorId = f.InvestorID ?? string.Empty,
            Direction = (char)f.PosiDirection switch
            {
                '2' => Direction.Buy,    // Long
                '3' => Direction.Sell,   // Short
                _ => Direction.Buy       // Net 视为多头（罕见）
            },
            HedgeFlag = (char)f.HedgeFlag switch
            {
                '1' => HedgeFlag.Speculation,
                '2' => HedgeFlag.Arbitrage,
                '3' => HedgeFlag.Hedge,
                _ => HedgeFlag.Speculation
            },
            TodayPosition = f.TodayPosition,
            YdPosition = f.YdPosition,
            TotalPosition = f.Position,
            FrozenPosition = f.LongFrozen + f.ShortFrozen,
            PositionCost = (decimal)f.PositionCost,
            PositionProfit = (decimal)f.PositionProfit,
            VolumeMultiple = 0  // CThostFtdcInvestorPositionField 无此字段，由 InstrumentStream 提供
        };
    }

    /// <summary>
    /// CTP TradingAccountField → Domain TradingAccount。
    /// CTP 无 <c>WithdrawBalance</c>/<c>Equity</c>/<c>MarketValue</c> 直接字段，按 Domain 注释公式换算：
    /// <see cref="TradingAccount.WithdrawBalance"/> = <c>Withdraw</c>（当日出金）；
    /// <see cref="TradingAccount.Equity"/> = <c>Balance</c> - <c>Withdraw</c>；
    /// <see cref="TradingAccount.MarketValue"/> = <c>CurrMargin</c> + <c>PositionProfit</c>（保证金占用 + 浮动盈亏 ≈ 持仓市值）。
    /// </summary>
    private static TradingAccount MapToDomain(in CThostFtdcTradingAccountField f)
    {
        var withdraw = (decimal)f.Withdraw;
        var balance = (decimal)f.Balance;
        return new TradingAccount
        {
            AccountId = f.AccountID ?? string.Empty,
            Balance = balance,
            Available = (decimal)f.Available,
            Equity = balance - withdraw,
            MarketValue = (decimal)(f.CurrMargin + f.PositionProfit),
            PositionProfit = (decimal)f.PositionProfit,
            CloseProfit = (decimal)f.CloseProfit,
            Margin = (decimal)f.CurrMargin,
            FrozenMargin = (decimal)f.FrozenMargin,
            FrozenCash = (decimal)f.FrozenCash,
            FrozenCommission = (decimal)f.FrozenCommission,
            Commission = (decimal)f.Commission,
            WithdrawBalance = withdraw
        };
    }

    /// <summary>CTP InstrumentField → Domain Instrument（仅取关键字段：代码/交易所/名称/PriceTick/合约乘数）。</summary>
    private static Instrument MapToDomain(in CThostFtdcInstrumentField f)
    {
        return new Instrument
        {
            InstrumentId = f.InstrumentID ?? string.Empty,
            ExchangeId = f.ExchangeID ?? string.Empty,
            Name = f.InstrumentName ?? string.Empty,
            PriceTick = (decimal)f.PriceTick,
            VolumeMultiple = f.VolumeMultiple,
            ProductClass = f.ProductClass,
            StrikePrice = (decimal)f.StrikePrice,
            OptionsType = f.OptionsType,
            ExpireDate = f.ExpireDate ?? string.Empty,
            CreateDate = f.CreateDate ?? string.Empty
        };
    }

    private string NextOrderRef() => Interlocked.Increment(ref _orderRefSeq).ToString();

    private string TryGetTradingDay()
    {
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return string.Empty;
                return ThostTraderApiNative.GetTradingDay(_apiPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetTradingDay 异常");
            return string.Empty;
        }
    }

    private void EnsureFlowPath()
    {
        try
        {
            var full = Path.GetFullPath(_options.FlowPath);
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建 FlowPath 失败: {Path}", _options.FlowPath);
        }
    }

    private void TransitionTo(ConnectionState next)
    {
        CurrentState = next;
        try { _connection.OnNext(next); }
        catch (Exception ex) { _logger.LogError(ex, "ConnectionStream.OnNext 异常"); }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed == 1)
            throw new ObjectDisposedException(nameof(CtpTradingService));
    }
}
