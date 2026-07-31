using FluentAssertions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Domain.Tests.Trading;

public class LegacyTradingRuntimeTests
{
    [Theory]
    [InlineData(0, LegacyPriceLadderOrderPath.Standard)]
    [InlineData(1, LegacyPriceLadderOrderPath.Standard)]
    [InlineData(2, LegacyPriceLadderOrderPath.Alternate)]
    [InlineData(3, LegacyPriceLadderOrderPath.Alternate)]
    [InlineData(4, LegacyPriceLadderOrderPath.Standard)]
    public void Run_mode_uses_the_legacy_bitmask_for_order_path(int runMode, LegacyPriceLadderOrderPath expected)
    {
        new LegacyTradingRuntime(runMode).PriceLadderOrderPath.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void CbOc_persistence_is_limited_to_the_proven_xml_branches(int runMode, bool persists)
    {
        new LegacyTradingRuntime(runMode).PersistsCbOc.Should().Be(persists);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void B_close_open_order_cancellation_uses_the_runtime_condition(int runMode, bool cancels)
    {
        new LegacyTradingRuntime(runMode).ResolveBModeClosePolicy(cbOc: false)
            .CancelSameDirectionOpenOrders.Should().Be(cancels);
    }
}
