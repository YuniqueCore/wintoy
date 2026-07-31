using System.ComponentModel;
using System.Windows.Input;
using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Infrastructure.MarketData;
using FuturesTrader.Infrastructure.Trading;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="TradingViewModel"/> 单元测试：覆盖 InstrumentWindow 33 字段双向绑定（Hydrate/回写）
/// 和价格梯点价下单交互（左键=ValLeft 量、右键=ValRight 量；物理交易侧决定方向）。
/// <para>
/// 用 <see cref="SimulatedMarketDataService"/> + <see cref="MockTradingService"/> + 宽容校验链端到端验证，
/// 不引入 mock 框架。无 WPF Application 时 <see cref="TradingViewModel"/> 构造期 Subscribe 内联执行，
/// 行情 timer 启动但不干扰点价测试（只校验 Order VM 状态变更）。
/// </para>
/// </summary>
public class TradingViewModelTests
{
    private static OrderConfig PermissiveConfig => new()
    {
        RiskOpen = true,
        MaxInputCount = 0,
        MaxPositionCount = 0,
        Spck = false,
        Gzck = false,
        MaxCancelGz = 0,
        MaxCancelSp = 0,
        MaxCancelQq = 0
    };

    /// <summary>
    /// 构造测试用 TradingViewModel：注入宽容风控 + 宽容时段校验器，避免测试依赖真实交易时段/限额。
    /// </summary>
    private static TradingViewModel CreateVm(
        InstrumentWindow? config = null,
        MockTradingService? trading = null,
        SimulatedMarketDataService? marketData = null,
        LegacyTradingRuntime? legacyTradingRuntime = null)
    {
        trading ??= new MockTradingService(NullLogger<MockTradingService>.Instance);
        marketData ??= new SimulatedMarketDataService(tickIntervalMs: 60000,
            NullLogger<SimulatedMarketDataService>.Instance);
        var risk = new LocalRiskService(PermissiveConfig, NullLogger<LocalRiskService>.Instance);
        var validator = new OrderValidator(new AlwaysAllowSessionChecker(), risk);
        var options = Options.Create(new MarketDataOptions { PriceLadderLevels = 5 });

        return new TradingViewModel(
            config ?? new InstrumentWindow { InstrumentCode = "ag2608" },
            marketData,
            new StubKeyboardService(),
            new StubSoundService(),
            options,
            NullLogger<TradingViewModel>.Instance,
            trading,
            risk,
            validator,
            NullLogger<OrderViewModel>.Instance,
            legacyTradingRuntime);
    }

    // ── InstrumentWindow 33 字段双向绑定（Hydrate → 回写 round-trip）─────────

    [Fact]
    public void HydrateFromConfig_populates_all_bound_fields()
    {
        var config = new InstrumentWindow
        {
            InstrumentCode = "cu2609",
            ValLeft = 3,
            ValRight = 5,
            RowHeight = 14,
            RboA = true,
            RboB = false,
            CbNearby = true,
            CbOnlyOpen = true,
            NarrowMode = true,
            CbCntrbySprd = true,
            CbCntrbySprdEx = true,
            CbCdLock = true,
            CbBgds = false,
            CbZdtLock = false,
            CntrbySprdId = "ag2608",
            CntrbySprdPt = 7,
            CntrbySprdFctn = 2
        };

        var vm = CreateVm(config);

        vm.InstrumentCode.Should().Be("cu2609");
        vm.ValLeft.Should().Be(3);
        vm.ValRight.Should().Be(5);
        vm.RowHeight.Should().Be(14);
        vm.RboA.Should().BeTrue();
        vm.RboB.Should().BeFalse();
        vm.OrderPlacementMode.Should().Be(OrderPlacementMode.ReplaceSameDirection);
        vm.CbNearby.Should().BeTrue();
        vm.CbOnlyOpen.Should().BeTrue();
        vm.NarrowMode.Should().BeTrue();
        vm.CbCntrbySprd.Should().BeTrue();
        vm.CbCntrbySprdEx.Should().BeTrue();
        vm.CbCdLock.Should().BeTrue();
        vm.CbBgds.Should().BeFalse();
        vm.CbZdtLock.Should().BeFalse();
        vm.CntrbySprdId.Should().Be("ag2608");
        vm.CntrbySprdPt.Should().Be(7);
        vm.CntrbySprdFctn.Should().Be(2);
    }

    [Fact]
    public void ToInstrumentWindow_round_trips_all_bound_fields()
    {
        var original = new InstrumentWindow
        {
            InstrumentCode = "jd2609",
            GroupId = 3,
            Top = 100,
            Left = 200,
            Height = 800,
            Width = 300,
            ValLeft = 2,
            ValRight = 4,
            RowHeight = 16,
            RboA = true,
            RboB = false,
            CbNearby = true,
            CbOnlyOpen = false,
            NarrowMode = true,
            CbCntrbySprd = true,
            CbCntrbySprdEx = false,
            CbCdLock = true,
            CbBgds = false,
            CbZdtLock = false,
            CntrbySprdId = "cu2609",
            CntrbySprdPt = 10,
            CntrbySprdFctn = 3
        };

        var vm = CreateVm(original);

        // 模拟用户在 UI 修改部分字段
        vm.ValLeft = 9;
        vm.ValRight = 7;
        vm.CbOnlyOpen = true;
        vm.CbBgds = true;
        vm.CntrbySprdPt = 42;

        var roundTripped = vm.ToInstrumentWindow();

        // 用户修改的字段应反映新值
        roundTripped.ValLeft.Should().Be(9);
        roundTripped.ValRight.Should().Be(7);
        roundTripped.CbOnlyOpen.Should().BeTrue();
        roundTripped.CbBgds.Should().BeTrue();
        roundTripped.CntrbySprdPt.Should().Be(42);
        // 未修改的字段应保留原值
        roundTripped.InstrumentCode.Should().Be("jd2609");
        roundTripped.GroupId.Should().Be(3);
        roundTripped.Top.Should().Be(100);
        roundTripped.Left.Should().Be(200);
        roundTripped.Height.Should().Be(800);
        roundTripped.Width.Should().Be(300);
        roundTripped.RowHeight.Should().Be(16);
        roundTripped.RboA.Should().BeTrue();
        roundTripped.RboB.Should().BeFalse();
        roundTripped.CbNearby.Should().BeTrue();
        roundTripped.NarrowMode.Should().BeTrue();
        roundTripped.CbCntrbySprd.Should().BeTrue();
        roundTripped.CbCntrbySprdEx.Should().BeFalse();
        roundTripped.CbCdLock.Should().BeTrue();
        roundTripped.CbZdtLock.Should().BeFalse();
        roundTripped.CntrbySprdId.Should().Be("cu2609");
        roundTripped.CntrbySprdFctn.Should().Be(3);
    }

    [Fact]
    public void Defaults_match_Users_xml_convention()
    {
        var vm = CreateVm();

        // 对齐 Users.xml 实证默认值（InstrumentWindow 默认值应与 Users.xml 一致）
        vm.ValLeft.Should().Be(1);
        vm.ValRight.Should().Be(2);
        vm.RowHeight.Should().Be(12);
        vm.RboB.Should().BeTrue();
        vm.OrderPlacementMode.Should().Be(OrderPlacementMode.Append);
        vm.CbBgds.Should().BeTrue();
        vm.CbZdtLock.Should().BeTrue();
        vm.CntrbySprdFctn.Should().Be(1);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Corrupt_legacy_radio_state_normalizes_to_safe_B_mode(bool rboA, bool rboB)
    {
        var vm = CreateVm(new InstrumentWindow { InstrumentCode = "ag2608", RboA = rboA, RboB = rboB });

        vm.OrderPlacementMode.Should().Be(OrderPlacementMode.Append);
        vm.RboA.Should().BeFalse();
        vm.RboB.Should().BeTrue();
    }

    [Fact]
    public void Changing_A_B_ui_mode_keeps_RBOA_RBOB_mutually_exclusive_for_persistence()
    {
        var vm = CreateVm();

        vm.IsChgOrderA = true;
        vm.OrderPlacementMode.Should().Be(OrderPlacementMode.ReplaceSameDirection);
        vm.RboA.Should().BeTrue();
        vm.RboB.Should().BeFalse();

        vm.IsChgOrderB = true;
        vm.OrderPlacementMode.Should().Be(OrderPlacementMode.Append);
        vm.RboA.Should().BeFalse();
        vm.RboB.Should().BeTrue();
        var persisted = vm.ToInstrumentWindow();
        persisted.RboA.Should().BeFalse();
        persisted.RboB.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, false, false)]
    public void CbOc_configuration_visibility_and_round_trip_follow_legacy_xml_branch(
        int runMode,
        bool expectedValue,
        bool expectedVisibility)
    {
        var vm = CreateVm(
            new InstrumentWindow { InstrumentCode = "ag2608", CbOc = true },
            legacyTradingRuntime: new LegacyTradingRuntime(runMode));

        vm.CbOc.Should().Be(expectedValue);
        vm.IsCbOcConfigurationPersisted.Should().Be(expectedVisibility);
        vm.ToInstrumentWindow().CbOc.Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Alternate_run_mode_blocks_price_ladder_order_instead_of_using_the_wrong_standard_path(int runMode)
    {
        var vm = CreateVm(legacyTradingRuntime: new LegacyTradingRuntime(runMode));

        await vm.OnPriceLeftClickedAsync(100m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.ActiveOrderCount.Should().Be(0);
        vm.Order.StatusMessage.Should().Contain($"RunMode={runMode}");
        vm.Order.StatusMessage.Should().Contain("已阻止报单");
    }

    // ── 物理交易侧点价下单（左键=ValLeft / 右键=ValRight）──────────────────

    [Fact]
    public async Task LeftClick_first_trade_side_places_Buy_order_with_ValLeft()
    {
        var vm = CreateVm();
        vm.ValLeft = 2;

        await vm.OnPriceLeftClickedAsync(100m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.Direction.Should().Be(Direction.Buy, "默认第一个交易列应路由为 Buy");
        vm.Order.Price.Should().Be(100m);
        vm.Order.Quantity.Should().Be(2, "应按 ValLeft 量下单");
        vm.Order.StatusMessage.Should().Contain("报单已提交");
    }

    [Fact]
    public async Task LeftClick_second_trade_side_places_Sell_order_with_ValLeft()
    {
        var vm = CreateVm();
        vm.ValLeft = 3;

        await vm.OnPriceLeftClickedAsync(200m, PriceLadderTradeSide.SecondTradeColumn);

        vm.Order.Direction.Should().Be(Direction.Sell, "默认第二个交易列应路由为 Sell");
        vm.Order.Price.Should().Be(200m);
        vm.Order.Quantity.Should().Be(3);
        vm.Order.StatusMessage.Should().Contain("报单已提交");
    }

    [Fact]
    public async Task LeftClick_unquoted_middle_row_still_uses_its_trade_side()
    {
        var vm = CreateVm();
        vm.ValLeft = 1;

        await vm.OnPriceLeftClickedAsync(150m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.Direction.Should().Be(Direction.Buy, "无人报价中间行不改变第一个交易列的方向");
        vm.Order.Price.Should().Be(150m);
    }

    [Fact]
    public async Task RightClick_first_trade_side_uses_ValRight_without_changing_direction()
    {
        var vm = CreateVm();
        vm.ValRight = 5;

        await vm.OnPriceRightClickedAsync(300m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.Direction.Should().Be(Direction.Buy);
        vm.Order.Price.Should().Be(300m);
        vm.Order.Quantity.Should().Be(5, "右键应按 ValRight 量下单");
        vm.Order.StatusMessage.Should().Contain("报单已提交");
    }

    [Fact]
    public async Task RightClick_second_trade_side_uses_ValRight_without_changing_direction()
    {
        var vm = CreateVm();
        vm.ValRight = 4;

        await vm.OnPriceRightClickedAsync(250m, PriceLadderTradeSide.SecondTradeColumn);

        vm.Order.Direction.Should().Be(Direction.Sell);
        vm.Order.Quantity.Should().Be(4);
        vm.Order.StatusMessage.Should().Contain("报单已提交");
    }

    // ── ValLeft/ValRight=0 时不下单（禁用点击下单）────────────────────────

    [Fact]
    public async Task LeftClick_with_ValLeft_zero_does_not_place_order()
    {
        var vm = CreateVm();
        vm.ValLeft = 0;

        await vm.OnPriceLeftClickedAsync(100m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.StatusMessage.Should().NotContain("报单已提交", "ValLeft=0 应跳过下单");
        vm.Order.StatusMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task RightClick_with_ValRight_zero_does_not_place_order()
    {
        var vm = CreateVm();
        vm.ValRight = 0;

        await vm.OnPriceRightClickedAsync(100m, PriceLadderTradeSide.SecondTradeColumn);

        vm.Order.StatusMessage.Should().NotContain("报单已提交", "ValRight=0 应跳过下单");
        vm.Order.StatusMessage.Should().BeEmpty();
    }

    // ── CbOnlyOpen 开关联动（开仓/平仓方向）──────────────────────────────

    [Fact]
    public async Task LeftClick_with_CbOnlyOpen_true_places_Open_order()
    {
        var vm = CreateVm();
        vm.CbOnlyOpen = true;
        vm.ValLeft = 1;

        await vm.OnPriceLeftClickedAsync(100m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.OffsetFlag.Should().Be(OffsetFlag.Open, "CbOnlyOpen=true 时应开仓");
    }

    [Fact]
    public async Task LeftClick_with_CbOnlyOpen_false_keeps_Open_when_no_reverse_position_exists()
    {
        var vm = CreateVm();
        vm.CbOnlyOpen = false;
        vm.ValLeft = 1;

        await vm.OnPriceLeftClickedAsync(100m, PriceLadderTradeSide.FirstTradeColumn);

        vm.Order.OffsetFlag.Should().Be(OffsetFlag.Open, "没有反向持仓时旧逻辑仍保留默认开仓");
    }

    // ── OpenCloseMark 联动 ─────────────────────────────────────────────

    [Fact]
    public void OpenCloseMark_reflects_CbOnlyOpen_state()
    {
        var vm = CreateVm();

        vm.CbOnlyOpen = true;
        vm.OpenCloseMark.Should().Be("O", "开仓模式显示 O");

        vm.CbOnlyOpen = false;
        vm.OpenCloseMark.Should().Be("P", "平仓模式显示 P");
    }

    [Fact]
    public void Changing_CbOnlyOpen_raises_OpenCloseMark_property_changed()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.CbOnlyOpen = true;

        changes.Should().Contain(nameof(TradingViewModel.OpenCloseMark));
    }

    // ── Dispose ────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_can_be_called_multiple_times()
    {
        var vm = CreateVm();

        var act = () =>
        {
            vm.Dispose();
            vm.Dispose();
        };

        act.Should().NotThrow("Dispose 应幂等");
    }

    // ── 期权 titlebar 格式化（FormatOptionsSuffix 纯函数）──────────────────

    [Fact]
    public void FormatOptionsSuffix_calculates_remaining_days_and_mmdd()
    {
        // 今天 2026-07-30，到期 2026-08-07 → 剩余 8 天
        TradingViewModel.FormatOptionsSuffix("20260807", new DateTime(2026, 7, 30))
            .Should().Be("[8天 0807]");
    }

    [Fact]
    public void FormatOptionsSuffix_today_expire_returns_zero_days()
    {
        TradingViewModel.FormatOptionsSuffix("20260730", new DateTime(2026, 7, 30))
            .Should().Be("[0天 0730]");
    }

    [Fact]
    public void FormatOptionsSuffix_past_expire_clamps_to_zero()
    {
        // 已过期：剩余天数不应为负
        TradingViewModel.FormatOptionsSuffix("20260729", new DateTime(2026, 7, 30))
            .Should().Be("[0天 0729]");
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("2026-08-07")]
    [InlineData("2026080")]
    public void FormatOptionsSuffix_invalid_date_returns_empty(string bad)
    {
        TradingViewModel.FormatOptionsSuffix(bad, new DateTime(2026, 7, 30))
            .Should().BeEmpty("无效日期应返回空字符串（不显示后缀）");
    }

    // ── InstrumentDisplayName 初始值（期货格式，元数据到达前）──────────────

    [Fact]
    public void InstrumentDisplayName_initial_futures_format_with_group()
    {
        var vm = CreateVm(new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 3 });
        vm.InstrumentDisplayName.Should().Be("ag2608 · 组 3");
    }

    [Fact]
    public void InstrumentDisplayName_initial_futures_format_without_group()
    {
        // GroupId=0 时不显示组号后缀
        var vm = CreateVm(new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 0 });
        vm.InstrumentDisplayName.Should().Be("ag2608");
    }
}

/// <summary>
/// 测试用键盘服务桩：所有方法 no-op，仅满足 <see cref="TradingViewModel"/> 构造依赖。
/// Register/Handle 不做任何事，MoveSelection 不改变 SelectedPriceIndex。
/// </summary>
internal sealed class StubKeyboardService : IKeyboardOperationService
{
    public int SelectedPriceIndex => -1;
    public event EventHandler<int>? SelectedPriceIndexChanged { add { } remove { } }
    public void Register(KeyGesture gesture, Action action, string? description = null) { }
    public void Unregister(KeyGesture gesture) { }
    public bool Handle(KeyEventArgs e) => false;
    public void MoveSelection(int offset, int maxIndex) { }
}

/// <summary>
/// 测试用提示音服务桩：Play/Enabled 均 no-op，仅满足 <see cref="TradingViewModel"/> 构造依赖。
/// </summary>
internal sealed class StubSoundService : ISoundService
{
    public bool Enabled { get; set; }
    public void Play(SoundType type) { }
}
