using System.Reactive.Linq;
using FluentAssertions;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Infrastructure.MarketData;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Infrastructure.Tests.MarketData;

/// <summary>
/// <see cref="SimulatedMarketDataService"/> Mock 行情服务测试：
/// ConnectAsync 立即 Connected、SubscribeAsync 后产出 tick、UnsubscribeAsync 停止推流、
/// MarketDataStream 过滤本合约。用短 tick 间隔（50ms）加速测试。
/// </summary>
public class SimulatedMarketDataServiceTests
{
    [Fact]
    public async Task ConnectAsync_transitions_to_connected_immediately()
    {
        await using var svc = new SimulatedMarketDataService(500, NullLogger<SimulatedMarketDataService>.Instance);
        svc.CurrentState.Should().BeOfType<ConnectionState.Disconnected>();
        await svc.ConnectAsync();
        svc.CurrentState.Should().BeOfType<ConnectionState.Connected>();
    }

    [Fact]
    public async Task SubscribeAsync_produces_ticks_for_subscribed_instrument()
    {
        await using var svc = new SimulatedMarketDataService(50, NullLogger<SimulatedMarketDataService>.Instance);
        await svc.ConnectAsync();
        var ticks = new List<DepthMarketData>();
        var sub = svc.MarketDataStream.Subscribe(ticks.Add);

        await svc.SubscribeAsync(new[] { "ag2608" });
        // 等待至少 2 个 tick（50ms × 4 = 200ms 留足余量）
        await Task.Delay(400);

        sub.Dispose();
        ticks.Should().NotBeEmpty("订阅后应产出 tick");
        ticks.Should().AllSatisfy(t => t.InstrumentId.Should().Be("ag2608"));
        ticks.Should().AllSatisfy(t => t.BidPrices.Should().HaveCount(5));
        ticks.Should().AllSatisfy(t => t.AskPrices.Should().HaveCount(5));
    }

    [Fact]
    public async Task UnsubscribeAsync_stops_ticks_for_instrument()
    {
        await using var svc = new SimulatedMarketDataService(50, NullLogger<SimulatedMarketDataService>.Instance);
        await svc.ConnectAsync();
        var ticks = new List<DepthMarketData>();
        var sub = svc.MarketDataStream.Subscribe(ticks.Add);

        await svc.SubscribeAsync(new[] { "ag2608" });
        await Task.Delay(200);
        var countBefore = ticks.Count;
        countBefore.Should().BeGreaterThan(0);

        await svc.UnsubscribeAsync(new[] { "ag2608" });
        await Task.Delay(200);
        var countAfter = ticks.Count;
        sub.Dispose();

        countAfter.Should().Be(countBefore, "退订后不应再产出该合约的 tick");
    }

    [Fact]
    public async Task SubscribeAsync_handles_unknown_instrument_with_fallback()
    {
        await using var svc = new SimulatedMarketDataService(50, NullLogger<SimulatedMarketDataService>.Instance);
        await svc.ConnectAsync();
        var ticks = new List<DepthMarketData>();
        var sub = svc.MarketDataStream.Subscribe(ticks.Add);

        await svc.SubscribeAsync(new[] { "UNKNOWN_CODE" });
        await Task.Delay(200);
        sub.Dispose();

        ticks.Should().NotBeEmpty("未知合约用默认 PriceTick 兜底，仍应产出 tick");
        ticks.Should().AllSatisfy(t => t.InstrumentId.Should().Be("UNKNOWN_CODE"));
    }

    [Fact]
    public async Task ConnectionStream_emits_connecting_then_connected()
    {
        await using var svc = new SimulatedMarketDataService(500, NullLogger<SimulatedMarketDataService>.Instance);
        var states = new List<ConnectionState>();
        var sub = svc.ConnectionStream.Subscribe(states.Add);
        await svc.ConnectAsync();
        sub.Dispose();

        states.Should().HaveCountGreaterThanOrEqualTo(2);
        states[0].Should().BeOfType<ConnectionState.Connecting>();
        states.Should().Contain(s => s is ConnectionState.Connected);
    }

    [Fact]
    public async Task DisconnectAsync_transitions_back_to_disconnected()
    {
        await using var svc = new SimulatedMarketDataService(500, NullLogger<SimulatedMarketDataService>.Instance);
        await svc.ConnectAsync();
        svc.CurrentState.Should().BeOfType<ConnectionState.Connected>();
        await svc.DisconnectAsync();
        svc.CurrentState.Should().BeOfType<ConnectionState.Disconnected>();
    }

    [Fact]
    public async Task Multiple_subscriptions_produce_ticks_for_all_instruments()
    {
        await using var svc = new SimulatedMarketDataService(50, NullLogger<SimulatedMarketDataService>.Instance);
        await svc.ConnectAsync();
        var ticks = new List<DepthMarketData>();
        var sub = svc.MarketDataStream.Subscribe(ticks.Add);

        await svc.SubscribeAsync(new[] { "ag2608", "cu2609", "jd2609" });
        await Task.Delay(400);
        sub.Dispose();

        ticks.Select(t => t.InstrumentId).Should().Contain(new[] { "ag2608", "cu2609", "jd2609" });
    }

    [Fact]
    public async Task Snapshot_contains_realistic_cumulative_and_order_book_fields()
    {
        await using var service = new SimulatedMarketDataService(
            20,
            NullLogger<SimulatedMarketDataService>.Instance,
            randomSeed: 42);
        await service.ConnectAsync();
        var firstTick = new TaskCompletionSource<DepthMarketData>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = service.MarketDataStream.Subscribe(tick => firstTick.TrySetResult(tick));

        await service.SubscribeAsync(["au2610"]);
        var snapshot = await firstTick.Task.WaitAsync(TimeSpan.FromSeconds(2));

        snapshot.PreSettlementPrice.Should().BePositive();
        snapshot.OpenPrice.Should().BePositive();
        snapshot.HighestPrice.Should().BeGreaterThanOrEqualTo(Math.Max(snapshot.OpenPrice, snapshot.LastPrice));
        snapshot.LowestPrice.Should().BeLessThanOrEqualTo(Math.Min(snapshot.OpenPrice, snapshot.LastPrice));
        snapshot.UpperLimitPrice.Should().BeGreaterThan(snapshot.LastPrice);
        snapshot.LowerLimitPrice.Should().BeLessThan(snapshot.LastPrice);
        snapshot.Volume.Should().BePositive();
        snapshot.Turnover.Should().BePositive();
        snapshot.OpenInterest.Should().BePositive();
        snapshot.BidPrices.Should().BeInDescendingOrder();
        snapshot.AskPrices.Should().BeInAscendingOrder();
        snapshot.BidVolumes.Should().OnlyContain(volume => volume > 0);
        snapshot.AskVolumes.Should().OnlyContain(volume => volume > 0);
    }

    [Fact]
    public async Task Same_local_seed_generates_same_first_market_snapshot()
    {
        await using var first = new SimulatedMarketDataService(
            20,
            NullLogger<SimulatedMarketDataService>.Instance,
            randomSeed: 8675309);
        await using var second = new SimulatedMarketDataService(
            20,
            NullLogger<SimulatedMarketDataService>.Instance,
            randomSeed: 8675309);
        await first.ConnectAsync();
        await second.ConnectAsync();
        var firstTick = new TaskCompletionSource<DepthMarketData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTick = new TaskCompletionSource<DepthMarketData>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var firstSubscription = first.MarketDataStream.Subscribe(tick => firstTick.TrySetResult(tick));
        using var secondSubscription = second.MarketDataStream.Subscribe(tick => secondTick.TrySetResult(tick));

        await first.SubscribeAsync(["ag2608"]);
        await second.SubscribeAsync(["ag2608"]);
        var snapshots = await Task.WhenAll(
            firstTick.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            secondTick.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        snapshots[0].LastPrice.Should().Be(snapshots[1].LastPrice);
        snapshots[0].Volume.Should().Be(snapshots[1].Volume);
        snapshots[0].OpenInterest.Should().Be(snapshots[1].OpenInterest);
        snapshots[0].BidVolumes.Should().Equal(snapshots[1].BidVolumes);
        snapshots[0].AskVolumes.Should().Equal(snapshots[1].AskVolumes);
    }
}
