using System.Collections.Concurrent;
using System.Reactive.Subjects;
using FuturesTrader.Domain.MarketData;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.MarketData;

/// <summary>
/// <see cref="IMarketDataService"/> 的 Mock 实现：内置几个示例合约，用 <see cref="Timer"/>
/// 按可配间隔（默认 500ms）做随机游走 tick，产出 <see cref="DepthMarketData"/>（5 档买卖盘
/// 按 last price 对称生成）→ <see cref="MarketDataStream"/>（<see cref="Subject{T}"/> 热流）。
/// ConnectAsync 立即转 Connected，永不断线（不触发 Reconnecting），用于端到端验证。
/// 线程安全：订阅集合用 <see cref="ConcurrentDictionary{TKey,TValue}"/>，tick 回调通过 Subject 同步派发。
/// </summary>
public sealed class SimulatedMarketDataService : IMarketDataService
{
    private static readonly Instrument[] SeedInstruments =
    [
        new() { InstrumentId = "ag2608", ExchangeId = "SHFE", Name = "白银2608", PriceTick = 1m, VolumeMultiple = 15 },
        new() { InstrumentId = "ag2610", ExchangeId = "SHFE", Name = "白银2610", PriceTick = 1m, VolumeMultiple = 15 },
        new() { InstrumentId = "cu2609", ExchangeId = "SHFE", Name = "铜2609", PriceTick = 10m, VolumeMultiple = 5 },
        new() { InstrumentId = "jd2609", ExchangeId = "DCE", Name = "鸡蛋2609", PriceTick = 1m, VolumeMultiple = 5 },
        new() { InstrumentId = "au2610", ExchangeId = "SHFE", Name = "黄金2610", PriceTick = 0.02m, VolumeMultiple = 1000 }
    ];

    /// <summary>各合约初始中间价（接近真实价位，便于 UI 视觉验证）。</summary>
    private static readonly Dictionary<string, decimal> SeedMidPrices = new()
    {
        ["ag2608"] = 7450m,
        ["ag2610"] = 7520m,
        ["cu2609"] = 73200m,
        ["jd2609"] = 3350m,
        ["au2610"] = 556.80m
    };

    private readonly Subject<DepthMarketData> _marketData = new();
    private readonly Subject<ConnectionState> _connection = new();
    private readonly ConcurrentDictionary<string, Instrument> _subscribed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, decimal> _currentPrices = new(StringComparer.Ordinal);
    private readonly Random _random = new(); // Subject.OnNext 在 timer 回调线程；锁内使用避免竞态
    private readonly object _randomLock = new();
    private readonly int _tickIntervalMs;
    private readonly ILogger<SimulatedMarketDataService> _logger;
    private Timer? _timer;
    private int _disposed;

    public SimulatedMarketDataService(int tickIntervalMs = 500, ILogger<SimulatedMarketDataService>? logger = null)
    {
        _tickIntervalMs = tickIntervalMs > 0 ? tickIntervalMs : 500;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedMarketDataService>.Instance;
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
        TransitionTo(new ConnectionState.Connecting());
        // Mock 立即连接成功，无前置/认证流程
        TransitionTo(new ConnectionState.Connected());
        _logger.LogInformation("SimulatedMarketData 已连接（Mock）");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopTimer();
        _subscribed.Clear();
        _currentPrices.Clear();
        TransitionTo(new ConnectionState.Disconnected());
        _logger.LogInformation("SimulatedMarketData 已断开（Mock）");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        if (instrumentIds.Count == 0) return Task.CompletedTask;
        foreach (var id in instrumentIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            var seed = Array.Find(SeedInstruments, i => i.InstrumentId == id);
            if (seed is null)
            {
                // 未知合约：用默认 PriceTick=1 + 初始价 1000 兜底，保证订阅总能产出 tick
                seed = new Instrument { InstrumentId = id, ExchangeId = "MOCK", Name = id, PriceTick = 1m, VolumeMultiple = 1 };
            }
            _subscribed[id] = seed;
            _currentPrices.GetOrAdd(id, SeedMidPrices.TryGetValue(id, out var p) ? p : 1000m);
            _logger.LogDebug("订阅合约 {Id}（Mock）", id);
        }
        EnsureTimerRunning();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        foreach (var id in instrumentIds)
        {
            _subscribed.TryRemove(id, out _);
            _currentPrices.TryRemove(id, out _);
        }
        if (_subscribed.IsEmpty) StopTimer();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        StopTimer();
        TransitionTo(new ConnectionState.Disconnected());
        _marketData.OnCompleted();
        _connection.OnCompleted();
        await Task.CompletedTask;
    }

    /// <summary>确保 timer 在有订阅合约时运行。</summary>
    private void EnsureTimerRunning()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => OnTick(), null, _tickIntervalMs, _tickIntervalMs);
    }

    private void StopTimer()
    {
        if (_timer is null) return;
        _timer.Dispose();
        _timer = null;
    }

    /// <summary>每个 tick 对所有订阅合约做随机游走并推流。</summary>
    private void OnTick()
    {
        if (_subscribed.IsEmpty) return;
        foreach (var (id, instrument) in _subscribed)
        {
            if (!_currentPrices.TryGetValue(id, out var mid)) continue;
            // 随机游走：±3 tick 内偏移（保证视觉可见但不剧烈）
            int delta;
            lock (_randomLock) { delta = _random.Next(-3, 4); }
            var newMid = Math.Max(instrument.PriceTick, mid + delta * instrument.PriceTick);
            _currentPrices[id] = newMid;
            var snapshot = BuildSnapshot(instrument, newMid);
            _marketData.OnNext(snapshot);
        }
    }

    /// <summary>围绕 mid 价生成 5 档对称深度行情快照。</summary>
    private static DepthMarketData BuildSnapshot(Instrument instrument, decimal mid)
    {
        var tick = instrument.PriceTick;
        var bidPrices = new decimal[5];
        var bidVolumes = new int[5];
        var askPrices = new decimal[5];
        var askVolumes = new int[5];
        // 简单的伪随机量（基于 mid/取模），避免再引入 Random 锁开销
        for (int i = 0; i < 5; i++)
        {
            bidPrices[i] = mid - (i + 1) * tick;
            askPrices[i] = mid + (i + 1) * tick;
            bidVolumes[i] = 1 + (int)(mid % 23 + i * 7) % 50;
            askVolumes[i] = 1 + (int)(mid % 19 + i * 11) % 50;
        }
        return new DepthMarketData
        {
            InstrumentId = instrument.InstrumentId,
            TradingDay = DateTime.Today.ToString("yyyyMMdd"),
            LastPrice = mid,
            OpenPrice = mid,
            HighestPrice = mid + 3 * tick,
            LowestPrice = mid - 3 * tick,
            Volume = (long)(mid % 1000) + 100,
            Turnover = mid * 100,
            OpenInterest = (long)(mid % 50000) + 1000,
            UpperLimitPrice = mid + 50 * tick,
            LowerLimitPrice = mid - 50 * tick,
            AveragePrice = mid,
            UpdateTime = TimeOnly.FromDateTime(DateTime.Now),
            UpdateMillisec = DateTime.Now.Millisecond,
            BidPrices = bidPrices,
            BidVolumes = bidVolumes,
            AskPrices = askPrices,
            AskVolumes = askVolumes
        };
    }

    private void TransitionTo(ConnectionState next)
    {
        CurrentState = next;
        _connection.OnNext(next);
    }
}
