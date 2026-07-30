using System.Reactive.Subjects;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Trading;

/// <summary>
/// <see cref="ITradingService"/> 的 Mock 实现：离线模拟交易全链路，不依赖 CTP DLL。
/// 用于 UI 开发/测试/演示：Connect 立即就绪，SendOrder 推送 Accepted→Filled，
/// CancelOrder 推送 Canceled。与 <c>SimulatedMarketDataService</c> 对称。
/// </summary>
public sealed class MockTradingService : ITradingService
{
    private readonly Subject<OrderResult> _orders = new();
    private readonly Subject<Trade> _trades = new();
    private readonly Subject<Position> _positions = new();
    private readonly Subject<Instrument> _instruments = new();
    private readonly Subject<TradingAccount> _accounts = new();
    private readonly Subject<ConnectionState> _connection = new();
    private readonly ILogger<MockTradingService> _logger;
    private int _orderRefSeq;
    private int _disposed;

    public MockTradingService(ILogger<MockTradingService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MockTradingService>.Instance;
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

        // 模拟 Accepted
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            InstrumentId = request.InstrumentId,
            Direction = request.Direction,
            OffsetFlag = request.OffsetFlag,
            Price = request.Price,
            Volume = request.Volume,
            Status = new OrderStatus.Accepted(),
            InsertTime = TimeOnly.FromDateTime(DateTime.Now)
        });

        // 模拟 Filled（延迟 200ms）
        _ = Task.Run(async () =>
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            var tradeTime = TimeOnly.FromDateTime(DateTime.Now);
            _trades.OnNext(new Trade
            {
                OrderRef = orderRef,
                InstrumentId = request.InstrumentId,
                Direction = request.Direction,
                OffsetFlag = request.OffsetFlag,
                Price = request.Price,
                Volume = request.Volume,
                TradeTime = tradeTime
            });
            _orders.OnNext(new OrderResult
            {
                OrderRef = orderRef,
                InstrumentId = request.InstrumentId,
                Direction = request.Direction,
                OffsetFlag = request.OffsetFlag,
                Price = request.Price,
                Volume = request.Volume,
                VolumeTraded = request.Volume,
                VolumeRemaining = 0,
                Status = new OrderStatus.Filled(request.Volume),
                InsertTime = tradeTime
            });
        }, cancellationToken);

        _logger.LogInformation("MockTrading 报单：{Instrument} {Dir} {Offset} {Vol}@{Price} Ref={Ref}",
            request.InstrumentId, request.Direction, request.OffsetFlag, request.Volume, request.Price, orderRef);
        return Task.FromResult(orderRef);
    }

    /// <inheritdoc />
    public Task CancelOrderAsync(string orderRef, int frontId, int sessionId, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        ArgumentException.ThrowIfNullOrWhiteSpace(orderRef);

        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            FrontId = frontId,
            SessionId = sessionId,
            Status = new OrderStatus.Canceled(0),
            InsertTime = TimeOnly.FromDateTime(DateTime.Now)
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

        // 推 1 条假 Position（无实际持仓，TotalPosition=0；VolumeMultiple=1000 与 au 系列对齐）
        _positions.OnNext(new Position
        {
            InstrumentId = instrumentId ?? "au2512",
            InvestorId = "mock-investor",
            Direction = Direction.Buy,
            HedgeFlag = HedgeFlag.Speculation,
            TodayPosition = 0,
            YdPosition = 0,
            TotalPosition = 0,
            FrozenPosition = 0,
            PositionCost = 0m,
            PositionProfit = 0m,
            VolumeMultiple = 1000
        });

        _logger.LogInformation("MockTrading 查询持仓：{Instrument}", instrumentId ?? "(全量)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryInstrumentAsync(string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询合约");

        // 推 1 条假 Instrument（au2512：PriceTick=0.02, VolumeMultiple=1000）
        _instruments.OnNext(new Instrument
        {
            InstrumentId = instrumentId ?? "au2512",
            ExchangeId = "SHFE",
            Name = "黄金2512",
            PriceTick = 0.02m,
            VolumeMultiple = 1000
        });

        _logger.LogInformation("MockTrading 查询合约：{Instrument}", instrumentId ?? "(全量)");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task QueryTradingAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(MockTradingService));
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("交易服务未连接，无法查询资金");

        // 推 1 条假 TradingAccount（可用=权益=300000，无持仓无冻结）
        _accounts.OnNext(new TradingAccount
        {
            AccountId = "mock-account",
            Balance = 300000m,
            Available = 300000m,
            Equity = 300000m,
            MarketValue = 0m,
            PositionProfit = 0m,
            CloseProfit = 0m,
            Margin = 0m,
            FrozenMargin = 0m,
            FrozenCash = 0m,
            FrozenCommission = 0m,
            Commission = 0m,
            WithdrawBalance = 0m
        });

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
        return ValueTask.CompletedTask;
    }

    private void TransitionTo(ConnectionState next)
    {
        CurrentState = next;
        try { _connection.OnNext(next); }
        catch (Exception ex) { _logger.LogError(ex, "ConnectionStream.OnNext 异常"); }
    }
}
