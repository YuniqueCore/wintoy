using System.ComponentModel;
using System.Reactive.Subjects;
using System.Windows.Input;
using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.MarketData;
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
        ITradingService? trading = null,
        LocalRiskService? risk = null,
        string instrument = "ag2608")
    {
        trading ??= new MockTradingService(NullLogger<MockTradingService>.Instance);
        risk ??= new LocalRiskService(PermissiveConfig, NullLogger<LocalRiskService>.Instance);
        // 用宽容时段校验器构造 OrderValidator，避免测试依赖真实交易时段
        // （7 步校验链中的 session 检查在非交易时段会拒单，测试需绕开）
        var validator = new OrderValidator(new AlwaysAllowSessionChecker(), risk);
        return new OrderViewModel(
            instrument, trading, risk, validator,
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

        vm.StatusMessage.Should().Contain("撤单请求已提交");
        risk.CurrentCounters.SpCount.Should().Be(1, "撤单后应累加 SP 计数");
    }

    // ── 价格梯 A/B 生命周期 ─────────────────────────────────

    [Fact]
    public async Task A_mode_cancels_all_same_direction_orders_but_submits_replacement_only_after_last_tracked_cancel()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 100m, 1, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: true, nearbyEnabled: false, nearbyThresholdMs: 0);
        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 101m, 1, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: true, nearbyEnabled: false, nearbyThresholdMs: 0);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 102m, 2, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.ReplaceSameDirection, onlyOpen: true, nearbyEnabled: false, nearbyThresholdMs: 0);

        trading.CancelRequests.Should().HaveCount(2, "A 模式应对全部同方向活动订单发撤单请求");
        vm.PlacementLifecycle.Should().BeOfType<OrderPlacementLifecycle.AwaitingTrackedCancel>();
        trading.SentOrders.Should().HaveCount(2, "撤单请求阶段不能提前提交替换单");

        var replacementSubmitted = new TaskCompletionSource<OrderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        trading.OrderSubmitted += (_, request) =>
        {
            if (request.Price == 102m)
                replacementSubmitted.TrySetResult(request);
        };

        trading.PublishCanceled(trading.CancelRequests[0].OrderRef);
        trading.SentOrders.Should().HaveCount(2, "非被跟踪撤单回报不能释放替换单");

        trading.PublishCanceled(trading.CancelRequests[^1].OrderRef);
        var replacement = await replacementSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        replacement.Direction.Should().Be(Direction.Buy);
        replacement.Volume.Should().Be(2);
        vm.PlacementLifecycle.Should().BeOfType<OrderPlacementLifecycle.Ready>();
    }

    [Fact]
    public async Task B_mode_appends_same_direction_orders_without_automatic_cancellation()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Sell, 100m, 1, PriceLadderTradeSide.SecondTradeColumn,
            OrderPlacementMode.Append, onlyOpen: true, nearbyEnabled: false, nearbyThresholdMs: 0);
        await vm.PlacePriceLadderOrderAsync(
            Direction.Sell, 101m, 1, PriceLadderTradeSide.SecondTradeColumn,
            OrderPlacementMode.Append, onlyOpen: true, nearbyEnabled: false, nearbyThresholdMs: 0);

        trading.SentOrders.Should().HaveCount(2);
        trading.CancelRequests.Should().BeEmpty("普通 B 开仓路径应追加而非自动撤单");
    }

    [Fact]
    public async Task Only_open_disabled_uses_available_opposite_position_and_clamps_the_volume()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);
        trading.PublishPosition(new Position
        {
            InstrumentId = "ag2608",
            Direction = Direction.Sell,
            TodayPosition = 3,
            YdPosition = 4,
            FrozenPosition = 0,
            TotalPosition = 7
        });

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 100m, 5, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: false, nearbyEnabled: false, nearbyThresholdMs: 0);

        trading.SentOrders.Should().ContainSingle();
        trading.SentOrders[0].OffsetFlag.Should().Be(OffsetFlag.CloseToday);
        trading.SentOrders[0].Volume.Should().Be(3);
    }

    [Fact]
    public async Task B_mode_close_replaces_one_tracked_close_order_when_frozen_orders_cover_the_whole_opposite_position()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);
        trading.PublishPosition(new Position
        {
            InstrumentId = "ag2608",
            Direction = Direction.Sell,
            TodayPosition = 3,
            YdPosition = 2,
            FrozenPosition = 5,
            TotalPosition = 5
        });
        trading.PublishAccepted("close-today", Direction.Buy, OffsetFlag.CloseToday, volume: 3);
        trading.PublishAccepted("close-yesterday", Direction.Buy, OffsetFlag.CloseYesterday, volume: 2);

        var replacementSubmitted = new TaskCompletionSource<OrderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        trading.OrderSubmitted += (_, request) => replacementSubmitted.TrySetResult(request);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 101m, 1, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: false, nearbyEnabled: false, nearbyThresholdMs: 0);

        trading.CancelRequests.Should().ContainSingle().Which.OrderRef.Should().Be("close-today");
        vm.PlacementLifecycle.Should().BeOfType<OrderPlacementLifecycle.AwaitingTrackedCancel>();
        trading.SentOrders.Should().BeEmpty("容量已满时，旧 B 路径先等待一笔平仓单撤回");

        trading.PublishCanceled("close-yesterday");
        trading.SentOrders.Should().BeEmpty("非被跟踪撤单回报不能释放替换单");

        trading.PublishCanceled("close-today");
        var replacement = await replacementSubmitted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        replacement.OffsetFlag.Should().Be(OffsetFlag.CloseToday);
        replacement.Volume.Should().Be(1);
        vm.PlacementLifecycle.Should().BeOfType<OrderPlacementLifecycle.Ready>();
    }

    [Fact]
    public async Task B_mode_close_aborts_deferred_replacement_when_tracked_order_fills()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);
        trading.PublishPosition(new Position
        {
            InstrumentId = "ag2608",
            Direction = Direction.Sell,
            TodayPosition = 3,
            YdPosition = 2,
            FrozenPosition = 5,
            TotalPosition = 5
        });
        trading.PublishAccepted("close-today", Direction.Buy, OffsetFlag.CloseToday, volume: 3);
        trading.PublishAccepted("close-yesterday", Direction.Buy, OffsetFlag.CloseYesterday, volume: 2);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 101m, 1, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: false, nearbyEnabled: false, nearbyThresholdMs: 0);
        trading.PublishFilled("close-today", filledVolume: 3);

        vm.PlacementLifecycle.Should().BeOfType<OrderPlacementLifecycle.Ready>();
        trading.SentOrders.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("B 模式平仓替换未提交");
    }

    [Fact]
    public async Task B_mode_close_with_CBOC_disabled_requests_cancellation_of_same_direction_open_orders_before_appending_close()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);
        trading.PublishPosition(new Position
        {
            InstrumentId = "ag2608",
            Direction = Direction.Sell,
            TodayPosition = 2,
            TotalPosition = 2
        });
        trading.PublishAccepted("opening-buy", Direction.Buy, OffsetFlag.Open, volume: 1);

        await vm.PlacePriceLadderOrderAsync(
            Direction.Buy, 101m, 1, PriceLadderTradeSide.FirstTradeColumn,
            OrderPlacementMode.Append, onlyOpen: false, nearbyEnabled: false, nearbyThresholdMs: 0,
            bModeClosePolicy: new BModeClosePolicy(CancelSameDirectionOpenOrders: true));

        trading.CancelRequests.Should().ContainSingle().Which.OrderRef.Should().Be("opening-buy");
        trading.SentOrders.Should().ContainSingle();
        trading.SentOrders[0].OffsetFlag.Should().Be(OffsetFlag.CloseToday);
    }

    [Fact]
    public void Order_updates_prefer_CTP_reported_remaining_volume_over_derived_volume()
    {
        var trading = new ManualTradingService();
        var vm = CreateVm(trading);
        trading.PublishAccepted("partial", Direction.Buy, OffsetFlag.CloseToday, volume: 9);

        trading.PublishPartiallyFilled("partial", volume: 9, volumeTraded: 1, volumeRemaining: 4);

        vm.ActiveOrders["partial"].RemainingVolume.Should().Be(4);
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

/// <summary>
/// 测试用宽容时段校验器：任何时刻都允许下单，避免 OrderViewModel 测试依赖真实交易时段。
/// 对齐 OrderValidatorTests.StubSessionChecker 的模式（默认放行）。
/// </summary>
internal sealed class AlwaysAllowSessionChecker : ITradingSessionChecker
{
    public bool IsInSession(DateTime now) => true;
    public bool CanPlaceOrder(DateTime now) => true;
    public (bool Allowed, string? Reason) CheckOrderAllowed(DateTime now) => (true, null);
    public TimeSpan TimeToNextSession(DateTime now) => TimeSpan.Zero;
}

/// <summary>
/// 可控交易假件：报单立即 Accepted，但撤单由测试显式发布 Canceled，
/// 用来验证 A 模式的异步等待状态，不依赖真实时间或 CTP。
/// </summary>
internal sealed class ManualTradingService : ITradingService
{
    private readonly Subject<OrderResult> _orders = new();
    private readonly Subject<Trade> _trades = new();
    private readonly Subject<Position> _positions = new();
    private readonly Subject<Instrument> _instruments = new();
    private readonly Subject<TradingAccount> _accounts = new();
    private readonly Subject<ConnectionState> _connections = new();
    private int _nextOrderRef;

    public List<OrderRequest> SentOrders { get; } = [];
    public List<(string OrderRef, int FrontId, int SessionId)> CancelRequests { get; } = [];
    public event EventHandler<OrderRequest>? OrderSubmitted;

    public ConnectionState CurrentState { get; } = new ConnectionState.Connected();
    public IObservable<OrderResult> OrderStream => _orders;
    public IObservable<Trade> TradeStream => _trades;
    public IObservable<Position> PositionStream => _positions;
    public IObservable<Instrument> InstrumentStream => _instruments;
    public IObservable<TradingAccount> AccountStream => _accounts;
    public IObservable<ConnectionState> ConnectionStream => _connections;

    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<string> SendOrderAsync(OrderRequest request, CancellationToken cancellationToken = default)
    {
        var orderRef = (++_nextOrderRef).ToString();
        SentOrders.Add(request with { OrderRef = orderRef });
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            FrontId = 1,
            SessionId = 1,
            InstrumentId = request.InstrumentId,
            Direction = request.Direction,
            OffsetFlag = request.OffsetFlag,
            Price = request.Price,
            Volume = request.Volume,
            Status = new OrderStatus.Accepted()
        });
        OrderSubmitted?.Invoke(this, SentOrders[^1]);
        return Task.FromResult(orderRef);
    }

    public Task CancelOrderAsync(string orderRef, int frontId, int sessionId, CancellationToken cancellationToken = default)
    {
        CancelRequests.Add((orderRef, frontId, sessionId));
        return Task.CompletedTask;
    }

    public Task QueryPositionAsync(string? instrumentId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task QueryInstrumentAsync(string? instrumentId = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task QueryTradingAccountAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void PublishCanceled(string orderRef) => _orders.OnNext(new OrderResult
    {
        OrderRef = orderRef,
        InstrumentId = "ag2608",
        Status = new OrderStatus.Canceled(0)
    });

    public void PublishAccepted(string orderRef, Direction direction, OffsetFlag offsetFlag, int volume) =>
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            FrontId = 1,
            SessionId = 1,
            InstrumentId = "ag2608",
            Direction = direction,
            OffsetFlag = offsetFlag,
            Price = 100m,
            Volume = volume,
            VolumeRemaining = volume,
            Status = new OrderStatus.Accepted()
        });

    public void PublishPartiallyFilled(string orderRef, int volume, int volumeTraded, int volumeRemaining) =>
        _orders.OnNext(new OrderResult
        {
            OrderRef = orderRef,
            InstrumentId = "ag2608",
            Volume = volume,
            VolumeTraded = volumeTraded,
            VolumeRemaining = volumeRemaining,
            Status = new OrderStatus.PartiallyFilled(volumeTraded)
        });

    public void PublishFilled(string orderRef, int filledVolume) => _orders.OnNext(new OrderResult
    {
        OrderRef = orderRef,
        InstrumentId = "ag2608",
        Volume = filledVolume,
        VolumeTraded = filledVolume,
        Status = new OrderStatus.Filled(filledVolume)
    });

    public void PublishPosition(Position position) => _positions.OnNext(position);

    public ValueTask DisposeAsync()
    {
        _orders.Dispose();
        _trades.Dispose();
        _positions.Dispose();
        _instruments.Dispose();
        _accounts.Dispose();
        _connections.Dispose();
        return ValueTask.CompletedTask;
    }
}
