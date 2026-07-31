using FluentAssertions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Domain.Tests.Trading;

/// <summary>覆盖旧 4C77B4/4C9144 已证实的 B 模式平仓容量替换规则。</summary>
public class BModeCloseReplacementPlannerTests
{
    private static readonly OppositePosition Opposite = new(TodayPosition: 3, YesterdayPosition: 2, FrozenPosition: 5);

    private static OrderRequest CloseTodayRequest(int volume = 1) => new()
    {
        InstrumentId = "ag2608",
        Direction = Direction.Buy,
        OffsetFlag = OffsetFlag.CloseToday,
        Price = 100m,
        Volume = volume,
        PriceTick = 1m
    };

    private static BModeActiveOrder Active(
        string orderRef,
        OffsetFlag offsetFlag,
        int remainingVolume,
        long sequence,
        bool cancellationRequested = false) => new(
        orderRef,
        Direction.Buy,
        offsetFlag,
        remainingVolume,
        sequence,
        cancellationRequested);

    [Fact]
    public void Open_request_never_creates_a_close_replacement_plan()
    {
        var request = CloseTodayRequest() with { OffsetFlag = OffsetFlag.Open };

        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            request,
            Opposite,
            [Active("ct", OffsetFlag.CloseToday, 3, 1), Active("cy", OffsetFlag.CloseYesterday, 2, 2)]);

        plan.Should().BeNull();
    }

    [Fact]
    public void Close_request_with_unused_capacity_appends_normally()
    {
        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            CloseTodayRequest(),
            Opposite,
            [Active("ct", OffsetFlag.CloseToday, 2, 1), Active("cy", OffsetFlag.CloseYesterday, 2, 2)]);

        plan.Should().BeNull("活动平仓量未恰好覆盖反向今昨仓位时，旧 B 路径继续直接追加");
    }

    [Fact]
    public void Saturated_close_capacity_tracks_exactly_one_earliest_eligible_close_order()
    {
        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            CloseTodayRequest(volume: 2),
            Opposite,
            [Active("ct", OffsetFlag.CloseToday, 3, 1), Active("cy", OffsetFlag.CloseYesterday, 2, 2)]);

        plan.Should().NotBeNull();
        plan!.TrackedOrderRef.Should().Be("ct");
        plan.PendingOrder.OffsetFlag.Should().Be(OffsetFlag.CloseToday);
        plan.PendingOrder.Volume.Should().Be(2);
    }

    [Fact]
    public void Already_requested_cancellation_is_not_selected_again()
    {
        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            CloseTodayRequest(),
            Opposite,
            [
                Active("already-canceling", OffsetFlag.CloseToday, 3, 1, cancellationRequested: true),
                Active("next", OffsetFlag.CloseYesterday, 2, 2)
            ]);

        plan.Should().NotBeNull();
        plan!.TrackedOrderRef.Should().Be("next");
        plan.PendingOrder.OffsetFlag.Should().Be(OffsetFlag.CloseYesterday);
    }

    [Fact]
    public void Partial_fill_remaining_volume_is_used_for_capacity_equality()
    {
        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            CloseTodayRequest(),
            Opposite,
            [Active("ct", OffsetFlag.CloseToday, 1, 1), Active("cy", OffsetFlag.CloseYesterday, 2, 2)]);

        plan.Should().BeNull("3 手今仓中已有订单只剩 1 手，活动平仓总量没有覆盖全部 5 手");
    }

    [Fact]
    public void Replacement_volume_is_capped_to_the_released_offset_bucket()
    {
        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen: false,
            CloseTodayRequest(volume: 9),
            Opposite,
            [Active("ct", OffsetFlag.CloseToday, 3, 1), Active("cy", OffsetFlag.CloseYesterday, 2, 2)]);

        plan.Should().NotBeNull();
        plan!.PendingOrder.Volume.Should().Be(3);
    }
}
