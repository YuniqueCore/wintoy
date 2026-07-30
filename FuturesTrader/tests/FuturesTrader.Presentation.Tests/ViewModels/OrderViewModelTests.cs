using System.ComponentModel;
using System.Windows.Input;
using FluentAssertions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.Trading;
using FuturesTrader.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="OrderViewModel"/> 单元测试：覆盖命令可用性、报单提交流程、风控拒绝、撤单、状态回报。
/// 用真实 <see cref="MockTradingService"/> + <see cref="LocalRiskService"/>（OrderConfig 全部放行）端到端验证，
/// 避免引入 mock 框架；OrderStream 推送后 VM 内 MarshalToUi 在无 WPF Application 时内联执行，测试可直接断言。
/// </summary>
public class OrderViewModelTests
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

    private static OrderViewModel CreateVm(
        MockTradingService? trading = null,
        LocalRiskService? risk = null,
        string instrument = "ag2608")
    {
        trading ??= new MockTradingService(NullLogger<MockTradingService>.Instance);
        risk ??= new LocalRiskService(PermissiveConfig, NullLogger<LocalRiskService>.Instance);
        return new OrderViewModel(
            instrument, trading, risk,
            NullLogger<OrderViewModel>.Instance);
    }

    // ── 初始状态与命令可用性 ─────────────────────────────────

    [Fact]
    public void Initial_state_has_default_values()
    {
        var vm = CreateVm();

        vm.Direction.Should().Be(Direction.Buy);
        vm.OffsetFlag.Should().Be(OffsetFlag.Open);
        vm.Quantity.Should().Be(1);
        vm.Price.Should().Be(0);
        vm.StatusMessage.Should().BeEmpty();
        vm.IsBusy.Should().BeFalse();
        vm.ActiveOrderCount.Should().Be(0);
    }

    [Fact]
    public void OrderCommand_disabled_when_price_zero()
    {
        var vm = CreateVm();
        vm.Quantity = 1;

        vm.OrderCommand.CanExecute(null).Should().BeFalse("价格为 0 时不可报单");
    }

    [Fact]
    public void OrderCommand_enabled_when_price_and_quantity_positive()
    {
        var vm = CreateVm();
        vm.Price = 100m;
        vm.Quantity = 1;

        vm.OrderCommand.CanExecute(null).Should().BeTrue("价格和数量 > 0 应可报单");
    }

    [Fact]
    public void OrderCommand_disabled_when_quantity_zero()
    {
        var vm = CreateVm();
        vm.Price = 100m;
        vm.Quantity = 0;

        vm.OrderCommand.CanExecute(null).Should().BeFalse("数量为 0 时不可报单");
    }

    [Fact]
    public void CancelCommand_disabled_when_no_active_orders()
    {
        var vm = CreateVm();

        vm.CancelCommand.CanExecute(null).Should().BeFalse("无活动报单时不可撤单");
    }

    // ── 报单提交流程 ────────────────────────────────────────

    [Fact]
    public async Task SendOrder_submits_to_trading_service_and_sets_status()
    {
        var trading = new MockTradingService(NullLogger<MockTradingService>.Instance);
        var vm = CreateVm(trading);
        vm.Price = 100m;
        vm.Quantity = 2;

        await vm.OrderCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("报单已提交");
        // MockTradingService 会立即推 Accepted，再延迟推 Filled；等待两者完成
        await Task.Delay(300);
        vm.StatusMessage.Should().Contain("全部成交");
    }

    [Fact]
    public async Task SendOrder_increments_session_order_count_for_risk_tracking()
    {
        var trading = new MockTradingService(NullLogger<MockTradingService>.Instance);
        // MaxInputCount=1：第二次报单应被风控拒绝（计数从 0 起算）
        var risk = new LocalRiskService(
            new OrderConfig { RiskOpen = true, MaxInputCount = 1 },
            NullLogger<LocalRiskService>.Instance);
        var vm = CreateVm(trading, risk);
        vm.Price = 100m;
        vm.Quantity = 1;

        await vm.OrderCommand.ExecuteAsync();
        var firstStatus = vm.StatusMessage;
        firstStatus.Should().Contain("报单已提交");

        // 第二次报单：会话报单计数已达 1，应被拒
        await vm.OrderCommand.ExecuteAsync();
        vm.StatusMessage.Should().Contain("报单数已达上限", "MaxInputCount=1 应拒绝第二次报单");
    }

    // ── 价格 tick 校验 ──────────────────────────────────────

    [Fact]
    public async Task SendOrder_rejects_price_not_multiple_of_PriceTick()
    {
        var vm = CreateVm();
        vm.PriceTick = 5m;
        vm.Price = 103m; // 不是 5 的倍数
        vm.Quantity = 1;

        await vm.OrderCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("整数倍");
    }

    [Fact]
    public async Task SendOrder_accepts_price_multiple_of_PriceTick()
    {
        var vm = CreateVm();
        vm.PriceTick = 5m;
        vm.Price = 105m;
        vm.Quantity = 1;

        await vm.OrderCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("报单已提交");
    }

    // ── 风控拒绝 ───────────────────────────────────────────

    [Fact]
    public async Task SendOrder_blocked_when_RiskOpen_but_no_limit_set_does_not_throw()
    {
        // RiskOpen=true 但所有限额=0（不限制）应放行，验证总开关 + 不限制组合不误拒
        var risk = new LocalRiskService(
            new OrderConfig { RiskOpen = true, MaxInputCount = 0, MaxPositionCount = 0 },
            NullLogger<LocalRiskService>.Instance);
        var vm = CreateVm(risk: risk);
        vm.Price = 100m;
        vm.Quantity = 1;

        await vm.OrderCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("报单已提交", "限额=0 表示不限制，应放行");
    }

    [Fact]
    public async Task SendOrder_records_status_message_on_exception()
    {
        // 用已 Dispose 的 trading 触发异常路径
        var trading = new MockTradingService(NullLogger<MockTradingService>.Instance);
        await trading.DisposeAsync();
        var vm = CreateVm(trading: trading);
        vm.Price = 100m;
        vm.Quantity = 1;

        await vm.OrderCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("报单失败");
        vm.IsBusy.Should().BeFalse("异常后应清理忙碌状态");
    }

    // ── 撤单流程 ───────────────────────────────────────────

    [Fact]
    public async Task CancelCommand_enabled_after_order_accepted()
    {
        var trading = new MockTradingService(NullLogger<MockTradingService>.Instance);
        var vm = CreateVm(trading);
        vm.Price = 100m;
        vm.Quantity = 1;
        await vm.OrderCommand.ExecuteAsync();
        // 等待 Accepted 推送（MockTradingService 同步推）
        await Task.Delay(50);

        vm.CancelCommand.CanExecute(null).Should().BeTrue("有活动报单时应可撤单");
    }

    [Fact]
    public async Task CancelOrder_calls_CancelOrderAsync_and_records_cancel()
    {
        var trading = new MockTradingService(NullLogger<MockTradingService>.Instance);
        var risk = new LocalRiskService(
            new OrderConfig { RiskOpen = true, Spck = true, MaxCancelSp = 5 },
            NullLogger<LocalRiskService>.Instance);
        var vm = CreateVm(trading, risk);
        vm.Price = 100m;
        vm.Quantity = 1;
        await vm.OrderCommand.ExecuteAsync();
        await Task.Delay(50);

        await vm.CancelCommand.ExecuteAsync();

        vm.StatusMessage.Should().Contain("撤单已提交");
        risk.CurrentCounters.SpCount.Should().Be(1, "撤单后应累加 SP 计数");
    }

    // ── 属性变更通知 ───────────────────────────────────────

    [Fact]
    public void Setting_Price_raises_property_changed()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Price = 200m;

        changes.Should().Contain(nameof(OrderViewModel.Price));
    }

    [Fact]
    public void Setting_Direction_raises_property_changed()
    {
        var vm = CreateVm();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        vm.Direction = Direction.Sell;

        changes.Should().Contain(nameof(OrderViewModel.Direction));
    }

    // ── Dispose ────────────────────────────────────────────

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
}

/// <summary>扩展：让 <see cref="ICommand"/> 在测试中可 await。</summary>
internal static class CommandTestExtensions
{
    public static Task ExecuteAsync(this ICommand command, object? parameter = null)
    {
        command.Execute(parameter);
        // AsyncRelayCommand 内部 Task 已启动，等待一帧让同步部分完成
        return Task.Delay(10);
    }
}
