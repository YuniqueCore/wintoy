using System.Collections.Concurrent;
using System.Reactive.Subjects;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Infrastructure.Mock;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.MarketData;

/// <summary>
/// <see cref="IMarketDataService"/> 的确定性 Mock：使用共享合约目录和每合约独立 seed，
/// 按可配间隔生成带累计成交量、成交额、持仓量、高低价和扩展深度的随机游走行情。
/// 扩展深度仅用于 Mock，让默认 30 格乃至最大 100 格都能观察到动态量；真实 CTP 仍严格保留五档。
/// ConnectAsync 立即转 Connected，用于离线开发、实机 UI 和自动化回归。
/// </summary>
public sealed class SimulatedMarketDataService : IMarketDataService
{
    private const int DefaultSeed = 20_260_801;
    private const int MockDepthLevelCount = 100;

    private readonly Subject<DepthMarketData> _marketData = new();
    private readonly Subject<ConnectionState> _connection = new();
    private readonly ConcurrentDictionary<string, QuoteState> _subscribed = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _tickIntervalMs;
    private readonly int _randomSeed;
    private readonly ILogger<SimulatedMarketDataService> _logger;
    private Timer? _timer;
    private int _tickInProgress;
    private int _disposed;

    public SimulatedMarketDataService(
        int tickIntervalMs = 500,
        ILogger<SimulatedMarketDataService>? logger = null,
        int randomSeed = DefaultSeed)
    {
        _tickIntervalMs = tickIntervalMs > 0 ? tickIntervalMs : 500;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulatedMarketDataService>.Instance;
        _randomSeed = randomSeed;
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
        TransitionTo(new ConnectionState.Connecting());
        TransitionTo(new ConnectionState.Connected());
        _logger.LogInformation("SimulatedMarketData 已连接（确定性 Mock，Seed={Seed}）", _randomSeed);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        StopTimer();
        _subscribed.Clear();
        TransitionTo(new ConnectionState.Disconnected());
        _logger.LogInformation("SimulatedMarketData 已断开（Mock）");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (CurrentState is not ConnectionState.Connected)
            throw new InvalidOperationException("行情服务未连接，无法订阅合约");
        if (instrumentIds.Count == 0) return Task.CompletedTask;

        foreach (var id in instrumentIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            _subscribed.GetOrAdd(id, key =>
            {
                var profile = MockMarketCatalog.FindOrFallback(key);
                _logger.LogDebug("订阅合约 {Id} {Name}（Mock）", key, profile.Instrument.Name);
                return new QuoteState(profile, CombineSeed(_randomSeed, key));
            });
        }
        EnsureTimerRunning();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnsubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken cancellationToken = default)
    {
        foreach (var id in instrumentIds)
            _subscribed.TryRemove(id, out _);
        if (_subscribed.IsEmpty) StopTimer();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        StopTimer();
        _subscribed.Clear();
        TransitionTo(new ConnectionState.Disconnected());
        _marketData.OnCompleted();
        _connection.OnCompleted();
        return ValueTask.CompletedTask;
    }

    private void EnsureTimerRunning()
    {
        if (_timer is not null) return;
        _timer = new Timer(_ => OnTick(), null, _tickIntervalMs, _tickIntervalMs);
    }

    private void StopTimer()
    {
        var timer = Interlocked.Exchange(ref _timer, null);
        timer?.Dispose();
    }

    private void OnTick()
    {
        if (_disposed == 1 || _subscribed.IsEmpty) return;
        if (Interlocked.Exchange(ref _tickInProgress, 1) == 1) return;

        try
        {
            foreach (var state in _subscribed.Values.OrderBy(item => item.Profile.Instrument.InstrumentId))
                _marketData.OnNext(state.NextSnapshot());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成 Mock 行情快照失败");
        }
        finally
        {
            Volatile.Write(ref _tickInProgress, 0);
        }
    }

    private void TransitionTo(ConnectionState next)
    {
        CurrentState = next;
        _connection.OnNext(next);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed == 1) throw new ObjectDisposedException(nameof(SimulatedMarketDataService));
    }

    private static int CombineSeed(int seed, string instrumentId)
    {
        unchecked
        {
            var combined = seed;
            foreach (var character in instrumentId.ToUpperInvariant())
                combined = combined * 31 + character;
            return combined;
        }
    }

    private sealed class QuoteState
    {
        private readonly Random _random;
        private decimal _lastPrice;
        private decimal _highestPrice;
        private decimal _lowestPrice;
        private long _volume;
        private decimal _turnover;
        private long _openInterest;
        private readonly int _unquotedRowCount;

        internal QuoteState(MockInstrumentProfile profile, int seed)
        {
            Profile = profile;
            _random = new Random(seed);
            _lastPrice = profile.InitialPrice;
            _highestPrice = Math.Max(profile.InitialPrice, profile.OpenPrice);
            _lowestPrice = Math.Min(profile.InitialPrice, profile.OpenPrice);
            _volume = profile.InitialVolume;
            _turnover = profile.InitialPrice * profile.InitialVolume * profile.Instrument.VolumeMultiple;
            _openInterest = profile.InitialOpenInterest;
            _unquotedRowCount = 3 + Math.Abs(seed % 5);
        }

        internal MockInstrumentProfile Profile { get; }

        internal DepthMarketData NextSnapshot()
        {
            var instrument = Profile.Instrument;
            var tick = instrument.PriceTick > 0 ? instrument.PriceTick : 1m;
            var upperLimit = RoundToTick(Profile.PreSettlementPrice * 1.10m, tick);
            var lowerLimit = RoundToTick(Profile.PreSettlementPrice * 0.90m, tick);

            var moveRoll = _random.Next(100);
            var deltaTicks = moveRoll switch
            {
                < 8 => -3,
                < 24 => -2,
                < 43 => -1,
                < 58 => 0,
                < 77 => 1,
                < 93 => 2,
                _ => 3,
            };
            _lastPrice = Math.Clamp(_lastPrice + deltaTicks * tick, lowerLimit, upperLimit);
            _highestPrice = Math.Max(_highestPrice, _lastPrice);
            _lowestPrice = Math.Min(_lowestPrice, _lastPrice);

            var tradeVolume = _random.Next(1, Math.Max(3, Profile.TypicalDepthVolume / 3));
            _volume += tradeVolume;
            _turnover += _lastPrice * tradeVolume * instrument.VolumeMultiple;
            _openInterest = Math.Max(100, _openInterest + _random.Next(-12, 18));

            var bidPrices = new decimal[MockDepthLevelCount];
            var bidVolumes = new int[MockDepthLevelCount];
            var askPrices = new decimal[MockDepthLevelCount];
            var askVolumes = new int[MockDepthLevelCount];
            var bidDistanceTicks = (_unquotedRowCount + 1) / 2;
            var askDistanceTicks = _unquotedRowCount + 1 - bidDistanceTicks;
            for (var index = 0; index < MockDepthLevelCount; index++)
            {
                bidPrices[index] = _lastPrice - (bidDistanceTicks + index) * tick;
                askPrices[index] = _lastPrice + (askDistanceTicks + index) * tick;
                var depthFloor = Math.Max(2, Profile.TypicalDepthVolume / (1 + index / 5));
                var jitter = Math.Max(1, depthFloor / 3);
                bidVolumes[index] = Math.Max(1, depthFloor + _random.Next(-jitter, jitter + 1));
                askVolumes[index] = Math.Max(1, depthFloor + _random.Next(-jitter, jitter + 1));
            }

            var now = DateTime.Now;
            return new DepthMarketData
            {
                InstrumentId = instrument.InstrumentId,
                TradingDay = now.ToString("yyyyMMdd"),
                LastPrice = _lastPrice,
                PreSettlementPrice = Profile.PreSettlementPrice,
                OpenPrice = Profile.OpenPrice,
                HighestPrice = _highestPrice,
                LowestPrice = _lowestPrice,
                Volume = _volume,
                Turnover = _turnover,
                OpenInterest = _openInterest,
                UpperLimitPrice = upperLimit,
                LowerLimitPrice = lowerLimit,
                AveragePrice = _volume > 0
                    ? _turnover / (_volume * instrument.VolumeMultiple)
                    : _lastPrice,
                UpdateTime = TimeOnly.FromDateTime(now),
                UpdateMillisec = now.Millisecond,
                BidPrices = bidPrices,
                BidVolumes = bidVolumes,
                AskPrices = askPrices,
                AskVolumes = askVolumes,
            };
        }

        private static decimal RoundToTick(decimal price, decimal tick) =>
            Math.Round(price / tick, MidpointRounding.AwayFromZero) * tick;
    }
}
