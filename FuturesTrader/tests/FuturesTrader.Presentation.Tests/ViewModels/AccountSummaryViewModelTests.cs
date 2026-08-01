using FluentAssertions;
using FuturesTrader.Infrastructure.Trading;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>账户摘要回放和多空分方向持仓聚合测试。</summary>
public sealed class AccountSummaryViewModelTests
{
    [Fact]
    public async Task Mock_initial_snapshots_populate_funding_and_sum_long_short_positions()
    {
        await using var trading = new MockTradingService(
            NullLogger<MockTradingService>.Instance,
            autoFillDelay: null);
        await trading.ConnectAsync();
        using var viewModel = new AccountSummaryViewModel(
            trading,
            NullLogger<AccountSummaryViewModel>.Instance);

        viewModel.Available.Should().Be(843_250m);
        viewModel.Equity.Should().Be(1_268_430m);
        viewModel.MarketValue.Should().Be(472_860m);
        viewModel.NetProfit.Should().Be(46_230m);
        viewModel.PositionLots.Should().Be(22,
            "ag2608 的 8 手多头和 2 手空头必须分别计入，而不是同合约互相覆盖");
    }
}
