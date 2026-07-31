using System.Collections.Concurrent;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.MarketData.Ctp;

/// <summary>
/// <see cref="IMarketDataService"/> 的 CTP 实现：直连 <c>thostmduserapi_se.dll</c> 6.7.13。
/// 完整链路：CreateFtdcMdApi → RegisterSpi(<see cref="CtpMdSpiBridge"/>) → RegisterFront → Init →
/// OnFrontConnected → ReqUserLogin → OnRspUserLogin → Connected → SubscribeMarketData →
/// OnRtnDepthMarketData → 映射为 <see cref="DepthMarketData"/> → <see cref="MarketDataStream"/>。
/// <para>
/// <b>线程模型</b>：CTP 回调在工作线程触发，<see cref="Subject{T}"/>.OnNext 线程安全可直调。
/// ConnectAsync 用 <see cref="TaskCompletionSource{TResult}"/> 等待 OnRspUserLogin（带 10s 超时）。
/// </para>
/// <para>
/// <b>断线重连</b>：CTP API 内置自动重连，OnFrontDisconnected 仅触发状态转换；
/// OnFrontConnected 在断线后再次触发时自动重新登录 + 重新订阅（对齐 0527.exe 5 秒重订阅行为）。
/// </para>
/// <para>
/// <b>SubscribeMarketData 参数构造</b>：CTP 要求 <c>char**</c>（指针数组），每个元素指向 GBK 编码的合约代码。
/// 用 <see cref="Marshal.AllocHGlobal"/> 分配数组 + 各字符串，调用后立即释放（CTP 内部同步拷贝）。
/// </para>
/// </summary>
public sealed class CtpMarketDataService : IMarketDataService
{
    private const double CtpNullPrice = 1.7976931348623157E+308; // DBL_MAX，CTP 表示"无值"
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly MarketDataOptions _options;
    private readonly ILogger<CtpMarketDataService> _logger;
    private readonly Subject<DepthMarketData> _marketData = new();
    private readonly Subject<ConnectionState> _connection = new();
    private readonly ConcurrentDictionary<string, bool> _subscribed = new(StringComparer.Ordinal);
    private readonly object _apiLock = new();

    private CtpMdSpiBridge? _spi;
    private IntPtr _apiPtr = IntPtr.Zero;
    private int _requestIdSeq;
    private int _disposed;
    private TaskCompletionSource<bool>? _loginTcs;
    private Task? _connectTask;

    public CtpMarketDataService(MarketDataOptions options, ILogger<CtpMarketDataService>? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<CtpMarketDataService>.Instance;

        if (string.IsNullOrWhiteSpace(_options.FrontAddress))
            throw new ArgumentException("MarketData:FrontAddress 未配置（Ctp 模式必填）", nameof(options));
        if (string.IsNullOrWhiteSpace(_options.FlowPath))
            throw new ArgumentException("MarketData:FlowPath 未配置", nameof(options));
    }

    /// <inheritdoc />
    public ConnectionState CurrentState { get; private set; } = new ConnectionState.Disconnected();

    /// <inheritdoc />
    public IObservable<DepthMarketData> MarketDataStream => _marketData;

    /// <inheritdoc />
    public IObservable<ConnectionState> ConnectionStream => _connection;

    /// <inheritdoc />
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_apiLock)
        {
            // 并发 Connect：复用同一登录任务，避免多 TradingViewModel 同时发起多次 Init
            if (_connectTask is { IsCompleted: false } pending) return pending;
            if (_apiPtr != IntPtr.Zero && CurrentState is ConnectionState.Connected)
            {
                _logger.LogDebug("CtpMarketData 已连接，跳过重复 Connect");
                return Task.CompletedTask;
            }

            TransitionTo(new ConnectionState.Connecting());

            // 1. 确保流文件目录存在（CTP 要求可写）
            EnsureFlowPath();

            // 2. 创建 SPI 桥接
            _spi = new CtpMdSpiBridge();
            _spi.FrontConnected += OnFrontConnected;
            _spi.FrontDisconnected += OnFrontDisconnected;
            _spi.RspUserLogin += OnRspUserLogin;
            _spi.RspError += OnRspError;
            _spi.DepthMarketDataReceived += OnDepthMarketData;

            // 3. 创建 API 实例（CreateFtdcMdApi 在 6.7.13 是 4 参数）
            _apiPtr = ThostMdApiNative.CreateFtdcMdApi(
                _options.FlowPath,
                _options.ApiRuntimeMode == CtpApiRuntimeMode.Production);
            if (_apiPtr == IntPtr.Zero)
            {
                TransitionTo(new ConnectionState.Failed("CreateFtdcMdApi 返回 null"));
                _spi.Dispose();
                _spi = null;
                throw new InvalidOperationException("CreateFtdcMdApi 失败，请检查 thostmduserapi_se.dll 是否在 PATH/输出目录");
            }

            _loginTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // 4. RegisterSpi → RegisterFront → Init（顺序固定）
            ThostMdApiNative.RegisterSpi(_apiPtr, _spi.SpiPointer);
            ThostMdApiNative.RegisterFront(_apiPtr, _options.FrontAddress);
            _logger.LogInformation("CtpMarketData Init 中：ApiRuntimeMode={ApiRuntimeMode} Flow={Flow}",
                _options.ApiRuntimeMode, _options.FlowPath);
            ThostMdApiNative.Init(_apiPtr);

            // 5. 异步等待 OnFrontConnected → ReqUserLogin → OnRspUserLogin（最长 10 秒）
            _connectTask = WaitForLoginAsync(cancellationToken);
            return _connectTask;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// 不调用 <see cref="ThrowIfDisposed"/>：Disconnect 是幂等的安全操作，
    /// 且 <see cref="DisposeAsync"/> 会先标记 _disposed=1 再调本方法，
    /// 若检查 _disposed 会抛 ObjectDisposedException 导致 Dispose 流程中断。
    /// </remarks>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero) return Task.CompletedTask;

            // 先解除 SPI 注册，避免 Release 过程中 CTP 还在回调我们的桥
            try { ThostMdApiNative.RegisterSpi(_apiPtr, IntPtr.Zero); }
            catch (Exception ex) { _logger.LogWarning(ex, "RegisterSpi(null) 异常（忽略）"); }

            try { ThostMdApiNative.Release(_apiPtr); }
            catch (Exception ex) { _logger.LogWarning(ex, "Release 异常（忽略）"); }

            _apiPtr = IntPtr.Zero;
            _spi?.Dispose();
            _spi = null;
            _loginTcs?.TrySetCanceled();
            _loginTcs = null;
            _connectTask = null;
        }

        _subscribed.Clear();
        TransitionTo(new ConnectionState.Disconnected());
        _logger.LogInformation("CtpMarketData 已断开");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (instrumentIds.Count == 0) return Task.CompletedTask;

        var newIds = new List<string>(instrumentIds.Count);
        foreach (var id in instrumentIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (_subscribed.TryAdd(id, true)) newIds.Add(id);
        }
        if (newIds.Count == 0) return Task.CompletedTask;

        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero)
            {
                _logger.LogWarning("Subscribe 时 API 未创建，合约已记录待重连后重订");
                return Task.CompletedTask;
            }
            CallSubscribe(newIds, isSubscribe: true);
        }
        _logger.LogInformation("订阅合约 {Count} 个: {Ids}", newIds.Count, string.Join(",", newIds));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (instrumentIds.Count == 0) return Task.CompletedTask;

        var removed = new List<string>(instrumentIds.Count);
        foreach (var id in instrumentIds)
        {
            if (_subscribed.TryRemove(id, out _)) removed.Add(id);
        }
        if (removed.Count == 0) return Task.CompletedTask;

        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero) return Task.CompletedTask;
            CallSubscribe(removed, isSubscribe: false);
        }
        _logger.LogInformation("退订合约 {Count} 个: {Ids}", removed.Count, string.Join(",", removed));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        try { await DisconnectAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogWarning(ex, "Dispose 时 Disconnect 异常"); }
        _marketData.OnCompleted();
        _connection.OnCompleted();
    }

    // ===== 回调处理（CTP 工作线程） =====

    private void OnFrontConnected()
    {
        _logger.LogInformation("CtpMarketData 前端已连接，发起 ReqUserLogin");
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return;
                // MdApi 登录无需 BrokerID/UserID/Password，传全 0 即可
                int reqId = Interlocked.Increment(ref _requestIdSeq);
                IntPtr pLogin = Marshal.AllocHGlobal(Marshal.SizeOf<CThostFtdcReqUserLoginField>());
                try
                {
                    Marshal.StructureToPtr(default(CThostFtdcReqUserLoginField), pLogin, fDeleteOld: false);
                    int ret = ThostMdApiNative.ReqUserLogin(_apiPtr, pLogin, reqId);
                    if (ret != 0)
                    {
                        _loginTcs?.TrySetException(new InvalidOperationException($"ReqUserLogin 返回 {ret}"));
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
            _logger.LogError(ex, "OnFrontConnected 处理异常");
            _loginTcs?.TrySetException(ex);
        }
    }

    private void OnFrontDisconnected(int nReason)
    {
        _logger.LogWarning("CtpMarketData 前端断开 Reason=0x{Reason:X}", nReason);
        // CTP API 自动重连，仅转状态；重连后 OnFrontConnected 会重新登录 + 重订阅
        TransitionTo(new ConnectionState.Reconnecting(1, TimeSpan.FromSeconds(5)));
    }

    private void OnRspUserLogin(bool success, string error)
    {
        if (success)
        {
            _logger.LogInformation("CtpMarketData 登录成功，交易日={Day}", TryGetTradingDay());
            TransitionTo(new ConnectionState.Connected());
            // 重连场景：自动补订阅
            ResubscribeAll();
            _loginTcs?.TrySetResult(true);
        }
        else
        {
            _logger.LogError("CtpMarketData 登录失败: {Error}", error);
            TransitionTo(new ConnectionState.Failed(error));
            _loginTcs?.TrySetException(new InvalidOperationException(error));
        }
    }

    private void OnRspError(int errorId, string errorMsg, int nRequestId)
    {
        _logger.LogError("CtpMarketData OnRspError: ErrorId={Id} Msg={Msg} ReqId={ReqId}", errorId, errorMsg, nRequestId);
    }

    private void OnDepthMarketData(IntPtr pDepthMarketData)
    {
        try
        {
            var field = Marshal.PtrToStructure<CThostFtdcDepthMarketDataField>(pDepthMarketData);
            var snapshot = MapToDomain(field);
            _marketData.OnNext(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnRtnDepthMarketData 映射失败");
        }
    }

    // ===== 内部辅助 =====

    private async Task WaitForLoginAsync(CancellationToken cancellationToken)
    {
        var tcs = _loginTcs;
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
            TransitionTo(new ConnectionState.Failed($"CtpMarketData 登录超时（{ConnectTimeout.TotalSeconds:F0}s）"));
            throw new TimeoutException($"CtpMarketData 登录超时（{ConnectTimeout.TotalSeconds:F0}s），请检查 FrontAddress 是否可达");
        }
    }

    private void ResubscribeAll()
    {
        var all = _subscribed.Keys.ToList();
        if (all.Count == 0) return;
        lock (_apiLock)
        {
            if (_apiPtr == IntPtr.Zero) return;
            CallSubscribe(all, isSubscribe: true);
            _logger.LogInformation("重连后自动重订阅 {Count} 个合约", all.Count);
        }
    }

    /// <summary>调用 SubscribeMarketData / UnSubscribeMarketData。在 _apiLock 内调用。</summary>
    private void CallSubscribe(IReadOnlyList<string> instrumentIds, bool isSubscribe)
    {
        if (_apiPtr == IntPtr.Zero || instrumentIds.Count == 0) return;

        // 构造 char** 数组：先分配指针数组，再为每个合约分配 GBK 字符串缓冲
        int count = instrumentIds.Count;
        IntPtr[] stringPtrs = new IntPtr[count];
        IntPtr arrayPtr = IntPtr.Zero;
        try
        {
            var gbk = CtpEncoding.GetGbkEncoding();
            for (int i = 0; i < count; i++)
            {
                // 合约代码 ASCII，但走 GBK 编码以保稳健（多字节合约名也能正确传输）
                byte[] bytes = gbk.GetBytes(instrumentIds[i]);
                stringPtrs[i] = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, stringPtrs[i], bytes.Length);
                Marshal.WriteByte(stringPtrs[i], bytes.Length, 0); // 终止符
            }

            arrayPtr = Marshal.AllocHGlobal(IntPtr.Size * count);
            Marshal.Copy(stringPtrs, 0, arrayPtr, count);

            int ret = isSubscribe
                ? ThostMdApiNative.SubscribeMarketData(_apiPtr, arrayPtr, count)
                : ThostMdApiNative.UnSubscribeMarketData(_apiPtr, arrayPtr, count);
            if (ret != 0)
            {
                _logger.LogWarning("{Op}MarketData 返回 {Ret}（contracts={Count}）",
                    isSubscribe ? "Subscribe" : "UnSubscribe", ret, count);
            }
        }
        finally
        {
            if (arrayPtr != IntPtr.Zero) Marshal.FreeHGlobal(arrayPtr);
            foreach (var p in stringPtrs)
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
        }
    }

    private string TryGetTradingDay()
    {
        try
        {
            lock (_apiLock)
            {
                if (_apiPtr == IntPtr.Zero) return string.Empty;
                return ThostMdApiNative.GetTradingDay(_apiPtr);
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
            // Path.GetFullPath 会把 "./MdFlow/" 解析为 cwd/MdFlow；CTP 要求目录存在且可写
            var full = Path.GetFullPath(_options.FlowPath);
            Directory.CreateDirectory(full);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建 FlowPath 失败: {Path}", _options.FlowPath);
        }
    }

    /// <summary>把 CTP native struct 映射为不可变 Domain record。价格 DBL_MAX → 0；时间 "HH:mm:ss" → TimeOnly。</summary>
    private static DepthMarketData MapToDomain(in CThostFtdcDepthMarketDataField f)
    {
        return new DepthMarketData
        {
            InstrumentId = f.InstrumentID ?? string.Empty,
            TradingDay = f.TradingDay ?? string.Empty,
            LastPrice = NormalizePrice(f.LastPrice),
            PreSettlementPrice = NormalizePrice(f.PreSettlementPrice),
            OpenPrice = NormalizePrice(f.OpenPrice),
            HighestPrice = NormalizePrice(f.HighestPrice),
            LowestPrice = NormalizePrice(f.LowestPrice),
            Volume = f.Volume,
            Turnover = NormalizePrice(f.Turnover),
            OpenInterest = NormalizePrice(f.OpenInterest),
            UpperLimitPrice = NormalizePrice(f.UpperLimitPrice),
            LowerLimitPrice = NormalizePrice(f.LowerLimitPrice),
            AveragePrice = NormalizePrice(f.AveragePrice),
            UpdateTime = ParseTime(f.UpdateTime),
            UpdateMillisec = f.UpdateMillisec,
            BidPrices = new[] {
                NormalizePrice(f.BidPrice1), NormalizePrice(f.BidPrice2), NormalizePrice(f.BidPrice3),
                NormalizePrice(f.BidPrice4), NormalizePrice(f.BidPrice5)
            },
            BidVolumes = new[] { f.BidVolume1, f.BidVolume2, f.BidVolume3, f.BidVolume4, f.BidVolume5 },
            AskPrices = new[] {
                NormalizePrice(f.AskPrice1), NormalizePrice(f.AskPrice2), NormalizePrice(f.AskPrice3),
                NormalizePrice(f.AskPrice4), NormalizePrice(f.AskPrice5)
            },
            AskVolumes = new[] { f.AskVolume1, f.AskVolume2, f.AskVolume3, f.AskVolume4, f.AskVolume5 }
        };
    }

    /// <summary>CTP 用 DBL_MAX 表示无值，转 0；其余 double → decimal。</summary>
    private static decimal NormalizePrice(double v) =>
        Math.Abs(v - CtpNullPrice) < 1e+300 ? 0m : (decimal)v;

    private static TimeOnly ParseTime(string s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        // CTP UpdateTime 是 "HH:mm:ss"，TimeOnly.Parse 容忍度足够
        return TimeOnly.TryParse(s, out var t) ? t : default;
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
            throw new ObjectDisposedException(nameof(CtpMarketDataService));
    }
}
