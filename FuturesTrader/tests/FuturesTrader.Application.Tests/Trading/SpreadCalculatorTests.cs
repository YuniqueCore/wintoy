using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;

namespace FuturesTrader.Application.Tests.Trading;

/// <summary>
/// SpreadCalculator 单元测试：覆盖价差价格计算、显示价格叠加、配置校验（互斥/空ID/无效tick/负系数）。
/// 对齐 0527.exe CntrbySprd 控件家族的价差计算公式（sub_4C4C5C / sub_4BC6C8）。
/// </summary>
public class SpreadCalculatorTests
{
    private readonly ISpreadCalculator _calculator = new SpreadCalculator();

    // ── CalculateSpreadPrice ─────────────────────────────────

    [Fact]
    public void CalculateSpreadPrice_adds_factor_times_tick_on_left_click()
    {
        // 基准价 300.0，系数 5，tick 0.2 → 300.0 + 5×0.2 = 301.0
        var result = _calculator.CalculateSpreadPrice(300.0m, 5, 0.2m, SpreadDirection.Add);

        result.Should().Be(301.0m);
    }

    [Fact]
    public void CalculateSpreadPrice_subtracts_factor_times_tick_on_right_click()
    {
        // 基准价 300.0，系数 5，tick 0.2 → 300.0 − 5×0.2 = 299.0
        var result = _calculator.CalculateSpreadPrice(300.0m, 5, 0.2m, SpreadDirection.Subtract);

        result.Should().Be(299.0m);
    }

    [Fact]
    public void CalculateSpreadPrice_zero_factor_returns_base_price()
    {
        // 系数 0 → 价差价格 = 基准价（无偏移）
        var result = _calculator.CalculateSpreadPrice(300.0m, 0, 0.2m, SpreadDirection.Add);

        result.Should().Be(300.0m);
    }

    [Fact]
    public void CalculateSpreadPrice_works_with_integer_tick()
    {
        // 股指期货：tick=0.2，基准价 4500，系数 3 → 4500 + 0.6 = 4500.6
        var result = _calculator.CalculateSpreadPrice(4500.0m, 3, 0.2m, SpreadDirection.Add);

        result.Should().Be(4500.6m);
    }

    [Fact]
    public void CalculateSpreadPrice_works_with_large_tick()
    {
        // 黄金：tick=0.01，基准价 500.50，系数 100 → 500.50 + 1.00 = 501.50
        var result = _calculator.CalculateSpreadPrice(500.50m, 100, 0.01m, SpreadDirection.Add);

        result.Should().Be(501.50m);
    }

    [Fact]
    public void CalculateSpreadPrice_throws_on_non_positive_tick()
    {
        var act = () => _calculator.CalculateSpreadPrice(300.0m, 5, 0m, SpreadDirection.Add);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("tickSize");
    }

    [Fact]
    public void CalculateSpreadPrice_throws_on_negative_factor()
    {
        var act = () => _calculator.CalculateSpreadPrice(300.0m, -1, 0.2m, SpreadDirection.Add);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("factor");
    }

    // ── CalculateDisplayPrice ────────────────────────────────

    [Fact]
    public void CalculateDisplayPrice_sums_all_three_components()
    {
        // ladderBase=300.0, spreadPrice=1.0, spreadInstrumentPrice=2500.0 → 2801.0
        var result = _calculator.CalculateDisplayPrice(300.0m, 1.0m, 2500.0m);

        result.Should().Be(2801.0m);
    }

    [Fact]
    public void CalculateDisplayPrice_with_zero_spread_instrument_returns_ladder_plus_spread()
    {
        // 无价差合约时 spreadInstrumentPrice=0
        var result = _calculator.CalculateDisplayPrice(300.0m, 1.0m, 0m);

        result.Should().Be(301.0m);
    }

    [Fact]
    public void CalculateDisplayPrice_with_negative_spread_instrument_decreases_price()
    {
        // 价差合约价格为负（理论上不应出现，但公式允许）
        var result = _calculator.CalculateDisplayPrice(300.0m, 1.0m, -0.5m);

        result.Should().Be(300.5m);
    }

    // ── Validate ─────────────────────────────────────────────

    [Fact]
    public void Validate_passes_when_no_spread_enabled()
    {
        var config = new SpreadConfig { TickSize = 0.2m };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void Validate_passes_when_normal_spread_enabled_with_instrument()
    {
        var config = new SpreadConfig
        {
            IsNormalEnabled = true,
            NormalInstrumentId = "au2512",
            Factor = 5,
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void Validate_passes_when_extended_spread_enabled_with_instrument()
    {
        var config = new SpreadConfig
        {
            IsExtendedEnabled = true,
            ExtendedInstrumentId = "au2510",
            Factor = 3,
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeTrue();
        reason.Should().BeNull();
    }

    [Fact]
    public void Validate_fails_when_both_normal_and_extended_enabled()
    {
        // 互斥校验：对齐 CBCntrbySprdClick / CBCntrbySprdEXClick 的互斥逻辑
        var config = new SpreadConfig
        {
            IsNormalEnabled = true,
            IsExtendedEnabled = true,
            NormalInstrumentId = "au2512",
            ExtendedInstrumentId = "au2510",
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeFalse();
        reason.Should().Contain("互斥");
    }

    [Fact]
    public void Validate_fails_when_normal_enabled_but_instrument_empty()
    {
        var config = new SpreadConfig
        {
            IsNormalEnabled = true,
            NormalInstrumentId = "",
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeFalse();
        reason.Should().Contain("普通价差").And.Contain("合约 ID");
    }

    [Fact]
    public void Validate_fails_when_extended_enabled_but_instrument_empty()
    {
        var config = new SpreadConfig
        {
            IsExtendedEnabled = true,
            ExtendedInstrumentId = "   ",
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeFalse();
        reason.Should().Contain("扩展价差").And.Contain("合约 ID");
    }

    [Fact]
    public void Validate_fails_on_non_positive_tick()
    {
        var config = new SpreadConfig
        {
            IsNormalEnabled = true,
            NormalInstrumentId = "au2512",
            TickSize = 0m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeFalse();
        reason.Should().Contain("最小变动价位");
    }

    [Fact]
    public void Validate_fails_on_negative_factor()
    {
        var config = new SpreadConfig
        {
            IsNormalEnabled = true,
            NormalInstrumentId = "au2512",
            Factor = -5,
            TickSize = 0.01m
        };

        var (valid, reason) = _calculator.Validate(config);

        valid.Should().BeFalse();
        reason.Should().Contain("系数");
    }

    // ── SpreadConfig.ActiveType ──────────────────────────────

    [Fact]
    public void ActiveType_is_none_when_nothing_enabled()
    {
        var config = new SpreadConfig();

        config.ActiveType.Should().Be(SpreadActiveType.None);
    }

    [Fact]
    public void ActiveType_is_normal_when_only_normal_enabled()
    {
        var config = new SpreadConfig { IsNormalEnabled = true };

        config.ActiveType.Should().Be(SpreadActiveType.Normal);
    }

    [Fact]
    public void ActiveType_is_extended_when_only_extended_enabled()
    {
        var config = new SpreadConfig { IsExtendedEnabled = true };

        config.ActiveType.Should().Be(SpreadActiveType.Extended);
    }
}
