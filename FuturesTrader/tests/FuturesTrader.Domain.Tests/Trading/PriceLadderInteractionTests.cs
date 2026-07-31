using FluentAssertions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Domain.Tests.Trading;

public class PriceLadderInteractionTests
{
    [Theory]
    [InlineData(PriceLadderTradeSide.FirstTradeColumn, Direction.Buy)]
    [InlineData(PriceLadderTradeSide.SecondTradeColumn, Direction.Sell)]
    public void Direction_map_uses_the_default_physical_column_mapping(PriceLadderTradeSide side, Direction expected)
    {
        new PriceLadderDirectionMap().Resolve(side).Should().Be(expected);
    }

    [Theory]
    [InlineData(PriceLadderTradeSide.FirstTradeColumn, Direction.Sell)]
    [InlineData(PriceLadderTradeSide.SecondTradeColumn, Direction.Buy)]
    public void Direction_map_can_reverse_the_physical_column_mapping(PriceLadderTradeSide side, Direction expected)
    {
        new PriceLadderDirectionMap(IsInverted: true).Resolve(side).Should().Be(expected);
    }

    [Fact]
    public void Awaiting_tracked_cancel_keeps_the_deferred_order_and_exact_order_reference()
    {
        var order = new OrderRequest
        {
            InstrumentId = "ag2608",
            Direction = Direction.Buy,
            OffsetFlag = OffsetFlag.Open,
            Price = 100m,
            Volume = 2
        };

        var state = new OrderPlacementLifecycle.AwaitingTrackedCancel(order, "42");

        state.PendingOrder.Should().Be(order);
        state.TrackedOrderRef.Should().Be("42");
    }
}
