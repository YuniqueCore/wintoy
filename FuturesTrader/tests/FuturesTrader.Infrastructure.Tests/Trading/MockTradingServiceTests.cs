using System.Reactive.Linq;
using FluentAssertions;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.Trading;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Infrastructure.Tests.Trading;

/// <summary>丰富 Mock 交易数据、订单生命周期与撤单互斥回归。</summary>
public sealed class MockTradingServiceTests
{
    [Fact]
    public async Task Connect_replays_rich_multi_exchange_instruments_positions_and_account()
    {
        await using var service = new MockTradingService(
            NullLogger<MockTradingService>.Instance,
            autoFillDelay: null);
        await service.ConnectAsync();
        var instruments = new List<Instrument>();
        var positions = new List<Position>();
        var accounts = new List<TradingAccount>();
        using var instrumentSubscription = service.InstrumentStream.Subscribe(instruments.Add);
        using var positionSubscription = service.PositionStream.Subscribe(positions.Add);
        using var accountSubscription = service.AccountStream.Subscribe(accounts.Add);

        instruments.Should().HaveCountGreaterThanOrEqualTo(25);
        instruments.Select(instrument => instrument.ExchangeId).Distinct().Should().HaveCountGreaterThanOrEqualTo(6);
        instruments.Should().Contain(instrument => instrument.IsOptions);
        instruments.Should().OnlyContain(instrument =>
            instrument.PriceTick > 0
            && instrument.VolumeMultiple > 0
            && instrument.MinLimitOrderVolume > 0
            && instrument.MaxLimitOrderVolume >= instrument.MinLimitOrderVolume
            && instrument.IsTrading);
        positions.Should().Contain(position => position.InstrumentId == "ag2608" && position.Direction == Direction.Buy);
        positions.Should().Contain(position => position.InstrumentId == "ag2608" && position.Direction == Direction.Sell);
        accounts.Should().ContainSingle();
        accounts[0].Available.Should().BePositive();
        accounts[0].MarketValue.Should().BePositive();
        accounts[0].Margin.Should().BePositive();
    }

    [Fact]
    public async Task Auto_fill_emits_accepted_partial_and_filled_with_trade_ids()
    {
        await using var service = new MockTradingService(
            NullLogger<MockTradingService>.Instance,
            TimeSpan.FromMilliseconds(100));
        await service.ConnectAsync();
        var orders = new List<OrderResult>();
        var trades = new List<Trade>();
        using var orderSubscription = service.OrderStream.Subscribe(orders.Add);
        using var tradeSubscription = service.TradeStream.Subscribe(trades.Add);

        var orderRef = await service.SendOrderAsync(new OrderRequest
        {
            InstrumentId = "ag2608",
            Direction = Direction.Buy,
            OffsetFlag = OffsetFlag.Open,
            Price = 7_330m,
            Volume = 4,
        });
        await Task.Delay(300);

        var lifecycle = orders.Where(order => order.OrderRef == orderRef).ToArray();
        lifecycle.Select(order => order.Status).Should().Contain(status => status is OrderStatus.Accepted);
        lifecycle.Select(order => order.Status).Should().Contain(status => status is OrderStatus.PartiallyFilled);
        lifecycle.Select(order => order.Status).Should().Contain(status => status is OrderStatus.Filled);
        trades.Where(trade => trade.OrderRef == orderRef).Sum(trade => trade.Volume).Should().Be(4);
        trades.Where(trade => trade.OrderRef == orderRef)
            .Should().OnlyContain(trade => !string.IsNullOrWhiteSpace(trade.TradeId) && trade.ExchangeId == "SHFE");
    }

    [Fact]
    public async Task Cancel_removes_active_order_and_prevents_late_auto_fill()
    {
        await using var service = new MockTradingService(
            NullLogger<MockTradingService>.Instance,
            TimeSpan.FromMilliseconds(160));
        await service.ConnectAsync();
        var orders = new List<OrderResult>();
        using var subscription = service.OrderStream.Subscribe(orders.Add);

        var orderRef = await service.SendOrderAsync(new OrderRequest
        {
            InstrumentId = "au2610",
            Direction = Direction.Sell,
            OffsetFlag = OffsetFlag.Open,
            Price = 556m,
            Volume = 2,
        });
        await service.CancelOrderAsync(orderRef, frontId: 1, sessionId: 1);
        await Task.Delay(300);

        var lifecycle = orders.Where(order => order.OrderRef == orderRef).ToArray();
        lifecycle.Select(order => order.Status).Should().Contain(status => status is OrderStatus.Accepted);
        lifecycle.Select(order => order.Status).Should().Contain(status => status is OrderStatus.Canceled);
        lifecycle.Any(order => order.Status is OrderStatus.PartiallyFilled or OrderStatus.Filled)
            .Should().BeFalse("撤单完成后不应再产生部分成交或全部成交回报");
    }
}
