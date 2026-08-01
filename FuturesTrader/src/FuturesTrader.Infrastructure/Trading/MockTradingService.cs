using System.Collections.Concurrent;
using System.Reactive.Subjects;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.Mock;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// <see cref="ITradingService"/> 的 Mock 实现：离线模拟交易全链路，不依赖 CTP DLL。
/// 用于 UI 开发/测试/演示：Connect 立即就绪，SendOrder 推送 Accepted，并可配置延迟 Filled；
/// CancelOrder 推送 Canceled。与 <c>SimulatedMarketDataService</c> 对称。
/// </summary>
public sealed class MockTradingService : ITradingService
{
    private readonly Subject<OrderResult> _orders = new();
    private readonly Subject<Trade> _trades = new();
    private readonly ReplaySubject<Position> _positions = new(MockMarketCatalog.Positions.Count);
    private readonly ReplaySubject<Instrument> _instruments = new(MockMarketCatalog.Profiles.Count);
    private readonly ReplaySubject<TradingAccount> _accounts = new(1);
    private readonly Subject<ConnectionState> _connection = new();
    private readonly ConcurrentDictionary<string, OrderRequest> _activeOrders = new(StringComparer.Ordinal);
    private readonly ILogger<MockTradingService> _logger;
    private readonly TimeSpan? _autoFillDelay;
    private int _orderRefSeq;
    private int _tradeIdSeq;
    // 模拟 CTP FrontID/SessionID 三元组定位：固定为 1（CTP 单实例下 FrontID/SessionID 永为 1），
    // 让 OrderViewModel 在 Accepted 时能回填 FrontId/SessionId，后续撤单流程可正确定位报单。
    private const int MockFrontId = 1;
    private const int MockSessionId = 1;
    private int _disposed;

    /// <summary>测试默认：200ms 后自动成交，保持既有快速状态机测试行为。</summary>
    public MockTradingService(ILogger<MockTradingService>? logger = null)
        : this(logger, TimeSpan.FromMilliseconds(200))
    {
    }

    /// <summary><paramref name="autoFillDelay"/> 为 null 时保持 Accepted，直到显式撤单。</summary>
    public MockTradingService(ILogger<MockTradingService>? logger, TimeSpan? autoFillDelay)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MockTradingService>.Instance;
        if (autoFillDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(autoFillDelay));
        _autoFillDelay = autoFillDelay;
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
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        TransitionTo(new ConnectionState.Connected());
        PublishInitialSnapshots();
        _logger.LogInformation("MockTrading 已连接（模拟）");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        TransitionTo(new ConnectionState.Disconnected());
        _logger.LogInformation("MockTrading 已断开");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string> SendOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        ArgumentNullException.ThrowIfNull(request);
        if (request.Volume <= 0)
            throw new ArgumentException("报单数量必须 > 0", nameof(request));

        string orderRef = request.OrderRef ?? Interlocked.Increment(ref _orderRefSeq).ToString();
        _activeOrders[orderRef] = request;
        var exchangeId = MockMarketCatalog.FindOrFallback(request.InstrumentId).Instrument.ExchangeId;

        // 模拟 Accepted（含 FrontId/SessionId，让 OrderViewModel 正确回填三元组，
        // 后续 CancelOrderAsync 才能定位报单。CTP 真实场景也是 Accepted 时同时推这三个字段）
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            FrontId = MockFrontId,
            SessionId = MockSessionId,
            InstrumentId = request.InstrumentId,
            Direction = request.Direction,
            OffsetFlag = request.OffsetFlag,
            Price = request.Price,
            Volume = request.Volume,
            VolumeRemaining = request.Volume,
            ExchangeId = exchangeId,
            Status = new OrderStatus.Accepted(),
            InsertTime = TimeOnly.FromDateTime(DateTime.Now),
            StatusMessage = "模拟交易所已接受，等待成交或撤单",
        });

        // 单元测试可配置延迟成交；UI Mock 传 null，保留挂单供价格梯数量/撤单实机验证。
        if (_autoFillDelay is { } delay)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var partiallyTraded = 0;
                    if (request.Volume > 1)
                    {
                        await Task.Delay(TimeSpan.FromTicks(delay.Ticks / 2), cancellationToken).ConfigureAwait(false);
                        if (!_activeOrders.ContainsKey(orderRef)) return;

                        partiallyTraded = Math.Max(1, request.Volume / 2);
                        PublishTrade(orderRef, request, partiallyTraded, exchangeId);
                        _orders.OnNext(new OrderResult
                        {
                            OrderRef = orderRef,
                            FrontId = MockFrontId,
                            SessionId = MockSessionId,
                            ExchangeId = exchangeId,
                            InstrumentId = request.InstrumentId,
                            Direction = request.Direction,
                            OffsetFlag = request.OffsetFlag,
                            Price = request.Price,
                            Volume = request.Volume,
                            VolumeTraded = partiallyTraded,
                            VolumeRemaining = request.Volume - partiallyTraded,
                            Status = new OrderStatus.PartiallyFilled(partiallyTraded),
                            InsertTime = TimeOnly.FromDateTime(DateTime.Now),
                            StatusMessage = $"模拟部分成交 {partiallyTraded}/{request.Volume}",
                        });
                        await Task.Delay(TimeSpan.FromTicks(delay.Ticks - delay.Ticks / 2), cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }

                    if (!_activeOrders.TryRemove(orderRef, out _)) return;
                    var remaining = request.Volume - partiallyTraded;
                    if (remaining > 0) PublishTrade(orderRef, request, remaining, exchangeId);
                    var tradeTime = TimeOnly.FromDateTime(DateTime.Now);
                    _orders.OnNext(new OrderResult
                    {
                        OrderRef = orderRef,
                        FrontId = MockFrontId,
                        SessionId = MockSessionId,
                        ExchangeId = exchangeId,
                        InstrumentId = request.InstrumentId,
                        Direction = request.Direction,
                        OffsetFlag = request.OffsetFlag,
                        Price = request.Price,
                        Volume = request.Volume,
                        VolumeTraded = request.Volume,
                        VolumeRemaining = 0,
                        Status = new OrderStatus.Filled(request.Volume),
                        InsertTime = tradeTime,
                        StatusMessage = $"模拟全部成交 {request.Volume}/{request.Volume}",
                    });
                }
                catch (OperationCanceledException)
                {
                    // 会话关闭/测试取消后不再产生迟到成交回报。
                }
            }, cancellationToken);
        }

        _logger.LogInformation("MockTrading 报单：{Instrument} {Dir} {Offset} {Vol}@{Price} Ref={Ref}",
            request.InstrumentId, request.Direction, request.OffsetFlag, request.Volume, request.Price, orderRef);
        return Task.FromResult(orderRef);
    }

    /// <inheritdoc />
    public Task CancelOrderAsync(string orderRef, int frontId, int sessionId, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        ArgumentException.ThrowIfNullOrWhiteSpace(orderRef);

        _activeOrders.TryRemove(orderRef, out var request);
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            FrontId = frontId,
            SessionId = sessionId,
            ExchangeId = request is null
                ? string.Empty
                : MockMarketCatalog.FindOrFallback(request.InstrumentId).Instrument.ExchangeId,
            InstrumentId = request?.InstrumentId ?? string.Empty,
            Direction = request?.Direction ?? default,
            OffsetFlag = request?.OffsetFlag ?? default,
            Price = request?.Price ?? 0,
            Volume = request?.Volume ?? 0,
            VolumeRemaining = request?.Volume ?? 0,
            Status = new OrderStatus.Canceled(0),
            InsertTime = TimeOnly.FromDateTime(DateTime.Now),
            StatusMessage = "模拟撤单成功",
        });

        _logger.LogInformation("MockTrading 撤单：Ref={Ref}", orderRef);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryPositionAsync(string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询持仓");

        var matches = instrumentId is null
            ? MockMarketCatalog.Positions
            : MockMarketCatalog.Positions
                .Where(position => position.InstrumentId.Equals(instrumentId, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (matches.Count == 0 && instrumentId is not null)
        {
            var profile = MockMarketCatalog.FindOrFallback(instrumentId);
            matches =
            [
                new Position
                {
                    InstrumentId = instrumentId,
                    InvestorId = "mock-investor",
                    Direction = Direction.Buy,
                    HedgeFlag = HedgeFlag.Speculation,
                    VolumeMultiple = profile.Instrument.VolumeMultiple,
                },
            ];
        }
        foreach (var position in matches) _positions.OnNext(position);

        _logger.LogInformation("MockTrading 查询持仓：{Instrument}", instrumentId ?? "(全量)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryInstrumentAsync(string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询合约");

        var instruments = instrumentId is null
            ? MockMarketCatalog.Profiles.Select(profile => profile.Instrument)
            : [MockMarketCatalog.FindOrFallback(instrumentId).Instrument];
        foreach (var instrument in instruments) _instruments.OnNext(instrument);

        _logger.LogInformation("MockTrading 查询合约：{Instrument}", instrumentId ?? "(全量)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryTradingAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询资金");

        _accounts.OnNext(MockMarketCatalog.Account);

        _logger.LogInformation("MockTrading 查询资金账户");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        _orders.OnCompleted();
        _trades.OnCompleted();
        _positions.OnCompleted();
        _instruments.OnCompleted();
        _accounts.OnCompleted();
        _connection.OnCompleted();
        _activeOrders.Clear();
        return ValueTask.CompletedTask;
    }

    private void PublishInitialSnapshots()
    {
        foreach (var profile in MockMarketCatalog.Profiles)
            _instruments.OnNext(profile.Instrument);
        foreach (var position in MockMarketCatalog.Positions)
            _positions.OnNext(position);
        _accounts.OnNext(MockMarketCatalog.Account);
    }

    private void PublishTrade(string orderRef, OrderRequest request, int volume, string exchangeId)
    {
        var now = DateTime.Now;
        _trades.OnNext(new Trade
        {
            TradeId = $"M{Interlocked.Increment(ref _tradeIdSeq):D8}",
            OrderRef = orderRef,
            InstrumentId = request.InstrumentId,
            Direction = request.Direction,
            OffsetFlag = request.OffsetFlag,
            Price = request.Price,
            Volume = volume,
            TradeTime = TimeOnly.FromDateTime(now),
            TradingDay = now.ToString("yyyyMMdd"),
            ExchangeId = exchangeId,
        });
    }

    private void TransitionTo(ConnectionState next)
    {
        CurrentState = next;
        try { _connection.OnNext(next); }
        catch (Exception ex) { _logger.LogError(ex, "ConnectionStream.OnNext 异常"); }
    }
}
