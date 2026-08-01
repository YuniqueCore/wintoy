using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 合约级订单编排器。手工面板可以直接提交订单；价格梯点击则通过
/// <see cref="PlacePriceLadderOrderAsync"/> 进入左右键数量、开平决策和 A/B 模式。
/// </summary>
public sealed partial class OrderViewModel : ObservableObject, IDisposable
{
    private sealed record ActiveOrder(
        int FrontId,
        int SessionId,
        decimal Price,
        Direction Direction,
        OffsetFlag OffsetFlag,
        int OriginalVolume,
        int RemainingVolume,
        long Sequence,
        bool CancellationRequested = false);

    private readonly ITradingService _trading;
    private readonly ILocalRiskService _risk;
    private readonly IOrderValidator _validator;
    private readonly ILogger<OrderViewModel> _logger;
    private readonly ConcurrentDictionary<string, ActiveOrder> _activeOrders = new();
    private readonly CompositeDisposable _subscriptions = new();
    private readonly SemaphoreSlim _placementGate = new(1, 1);
    private decimal _priceTick = 1m;
    private int _longTodayPosition;
    private int _longYesterdayPosition;
    private int _longFrozenPosition;
    private int _shortTodayPosition;
    private int _shortYesterdayPosition;
    private int _shortFrozenPosition;
    private DateTime? _firstTradeSideMarketUpdate;
    private DateTime? _secondTradeSideMarketUpdate;
    private OrderValidationContext? _deferredReplacementContext;
    private long _nextOrderSequence;
    private bool _disposed;

    public OrderViewModel(
        string instrumentCode,
        ITradingService trading,
        ILocalRiskService risk,
        IOrderValidator orderValidator,
        ILogger<OrderViewModel> logger)
    {
        InstrumentCode = instrumentCode;
        _trading = trading;
        _risk = risk;
        _validator = orderValidator;
        _logger = logger;

        OrderCommand = new AsyncRelayCommand(SendManualOrderAsync, CanSendOrder);
        CancelCommand = new AsyncRelayCommand(CancelLastOrderAsync, CanCancelOrder);
        _subscriptions.Add(_trading.OrderStream.Subscribe(OnOrderResult, OnStreamError));
        _subscriptions.Add(
            _trading.PositionStream
                .Where(position => string.Equals(position.InstrumentId, InstrumentCode, StringComparison.Ordinal))
                .Subscribe(OnPositionUpdate, OnStreamError));
    }

    /// <summary>活动订单变化，供价格梯按价格重新汇总第 0 列挂单数。</summary>
    public event EventHandler<OrderActiveStateChangedEventArgs>? ActiveOrdersChanged;

    /// <summary>活动订单的线程安全快照。</summary>
    public IReadOnlyDictionary<string, ActiveOrderInfo> ActiveOrders => _activeOrders.ToDictionary(
        pair => pair.Key,
        pair => new ActiveOrderInfo(
            pair.Value.Direction,
            pair.Value.OffsetFlag,
            pair.Value.Price,
            pair.Value.OriginalVolume,
            pair.Value.RemainingVolume,
            pair.Value.CancellationRequested));

    /// <summary>合约代码。</summary>
    public string InstrumentCode { get; }

    /// <summary>最小变动价位。</summary>
    public decimal PriceTick
    {
        get => _priceTick;
        set
        {
            if (SetProperty(ref _priceTick, value > 0 ? value : 1m))
                RefreshCommands();
        }
    }

    [ObservableProperty]
    public partial Direction Direction { get; set; } = Direction.Buy;

    [ObservableProperty]
    public partial OffsetFlag OffsetFlag { get; set; } = OffsetFlag.Open;

    [ObservableProperty]
    public partial decimal Price { get; set; }

    [ObservableProperty]
    public partial int Quantity { get; set; } = 1;

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>A 模式替换单的显式生命周期。</summary>
    [ObservableProperty]
    public partial OrderPlacementLifecycle PlacementLifecycle { get; private set; } = new OrderPlacementLifecycle.Ready();

    public IAsyncRelayCommand OrderCommand { get; }

    public IAsyncRelayCommand CancelCommand { get; }

    public int ActiveOrderCount => _activeOrders.Count;

    /// <summary>
    /// 由上层价格梯编排器报告已证实但尚未完整端口的旧版路径。此方法只更新可见反馈，
    /// 不会构造、发送或撤销任何订单。
    /// </summary>
    public void ReportPriceLadderOrderBlocked(string reason)
    {
        if (_disposed) return;
        StatusMessage = reason;
        _logger.LogWarning("价格梯报单已阻止：{Instrument} {Reason}", InstrumentCode, reason);
    }

    /// <summary>把价格梯入口的未预期异常转成用户可见反馈；异常详情仍由上层记录完整日志。</summary>
    public void ReportPriceLadderOrderFailure(string reason)
    {
        if (_disposed) return;
        StatusMessage = $"报单失败：{reason}";
    }

    partial void OnDirectionChanged(Direction value) => RefreshCommands();
    partial void OnOffsetFlagChanged(OffsetFlag value) => RefreshCommands();
    partial void OnPriceChanged(decimal value) => RefreshCommands();
    partial void OnQuantityChanged(int value) => RefreshCommands();

    /// <summary>
    /// 行情路径观察到某一物理交易侧更新时调用。该时间专供 CBNearby，绝不由鼠标点击写入。
    /// </summary>
    public void RecordMarketUpdate(PriceLadderTradeSide side, DateTime observedAt)
    {
        switch (side)
        {
            case PriceLadderTradeSide.FirstTradeColumn:
                _firstTradeSideMarketUpdate = observedAt;
                break;
            case PriceLadderTradeSide.SecondTradeColumn:
                _secondTradeSideMarketUpdate = observedAt;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side), side, "不是可交易的价格梯侧");
        }
    }

    /// <summary>
    /// 从价格梯提交订单。方向由物理交易侧的上层映射决定，左右键只在上层换算为数量。
    /// </summary>
    public async Task PlacePriceLadderOrderAsync(
        Direction direction,
        decimal price,
        int requestedQuantity,
        PriceLadderTradeSide side,
        OrderPlacementMode placementMode,
        bool onlyOpen,
        bool nearbyEnabled,
        int nearbyThresholdMs,
        BModeClosePolicy bModeClosePolicy = default)
    {
        if (_disposed || requestedQuantity <= 0) return;

        await _placementGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (PlacementLifecycle is not OrderPlacementLifecycle.Ready)
            {
                StatusMessage = "正在等待上一笔 A 模式替换撤单回报";
                return;
            }

            var resolution = CloseOrderResolver.Resolve(onlyOpen, requestedQuantity, GetOppositePosition(direction));
            if (resolution.Volume <= 0)
            {
                StatusMessage = "没有可用的反向持仓可平";
                return;
            }

            Direction = direction;
            OffsetFlag = resolution.OffsetFlag;
            Price = price;
            Quantity = resolution.Volume;

            var request = new OrderRequest
            {
                InstrumentId = InstrumentCode,
                Direction = direction,
                OffsetFlag = resolution.OffsetFlag,
                Price = price,
                Volume = resolution.Volume,
                PriceTick = _priceTick
            };
            var context = new OrderValidationContext
            {
                Now = DateTime.Now,
                ActiveOrderCount = ActiveOrderCount,
                CurrentPositionCount = CurrentPositionCount,
                NearbyEnabled = nearbyEnabled,
                NearbyThrottleMs = nearbyThresholdMs,
                LastRelevantMarketUpdate = GetLastMarketUpdate(side)
            };

            if (placementMode == OrderPlacementMode.Append)
            {
                await AppendOrderAsync(request, context, onlyOpen, bModeClosePolicy).ConfigureAwait(true);
                return;
            }

            await ReplaceSameDirectionAsync(request, context).ConfigureAwait(true);
        }
        finally
        {
            _placementGate.Release();
        }
    }

    /// <summary>手工面板报单，不自动套用价格梯 A/B 或 CBNearby 策略。</summary>
    private async Task SendManualOrderAsync()
    {
        if (_disposed) return;
        var request = new OrderRequest
        {
            InstrumentId = InstrumentCode,
            Direction = Direction,
            OffsetFlag = OffsetFlag,
            Price = Price,
            Volume = Quantity,
            PriceTick = _priceTick
        };
        var context = new OrderValidationContext
        {
            Now = DateTime.Now,
            ActiveOrderCount = ActiveOrderCount,
            CurrentPositionCount = CurrentPositionCount
        };
        await SubmitOrderAsync(request, context).ConfigureAwait(true);
    }

    /// <summary>
    /// A 模式：撤同合约同方向的所有活跃订单，等待序列中最后一笔的 Canceled 回报后提交替换单。
    /// 方向相反的订单不参与，因此多头和空头可同时存在。
    /// </summary>
    private async Task ReplaceSameDirectionAsync(OrderRequest request, OrderValidationContext context)
    {
        var matchingOrders = _activeOrders
            .Where(pair => pair.Value.Direction == request.Direction)
            .OrderBy(pair => pair.Value.Sequence)
            .ToArray();
        if (matchingOrders.Length == 0)
        {
            await SubmitOrderAsync(request, context).ConfigureAwait(true);
            return;
        }

        var trackedOrderRef = matchingOrders[^1].Key;
        PlacementLifecycle = new OrderPlacementLifecycle.AwaitingTrackedCancel(
            request, trackedOrderRef, DeferredReplacementCause.AModeSameDirection);
        _deferredReplacementContext = context;

        foreach (var (orderRef, order) in matchingOrders)
        {
            if (await RequestCancelAsync(orderRef, order, suppressStatusOnSuccess: true).ConfigureAwait(true))
                continue;

            PlacementLifecycle = new OrderPlacementLifecycle.Ready();
            _deferredReplacementContext = null;
            StatusMessage = "A 模式替换未提交：存在无法发起撤单的同方向挂单";
            return;
        }

        StatusMessage = "A 模式：等待被跟踪撤单回报";
    }

    /// <summary>
    /// B 模式：普通开仓直接追加。平仓时先应用 RunMode/CBOC 的已证实开仓撤单分支，
    /// 再在平今/平昨挂单总量恰好覆盖反向持仓时，仅替换一笔活动平仓单。
    /// </summary>
    private async Task AppendOrderAsync(
        OrderRequest request,
        OrderValidationContext context,
        bool onlyOpen,
        BModeClosePolicy bModeClosePolicy)
    {
        if (onlyOpen || request.OffsetFlag == OffsetFlag.Open)
        {
            await SubmitOrderAsync(request, context).ConfigureAwait(true);
            return;
        }

        if (bModeClosePolicy.CancelSameDirectionOpenOrders)
        {
            var openingOrders = _activeOrders
                .Where(pair => pair.Value.Direction == request.Direction
                    && pair.Value.OffsetFlag == OffsetFlag.Open)
                .OrderBy(pair => pair.Value.Sequence)
                .ToArray();
            foreach (var (orderRef, order) in openingOrders)
                await RequestCancelAsync(orderRef, order, suppressStatusOnSuccess: true).ConfigureAwait(true);
        }

        var plan = BModeCloseReplacementPlanner.TryPlan(
            onlyOpen,
            request,
            GetOppositePosition(request.Direction),
            _activeOrders.Select(pair => new BModeActiveOrder(
                pair.Key,
                pair.Value.Direction,
                pair.Value.OffsetFlag,
                pair.Value.RemainingVolume,
                pair.Value.Sequence,
                pair.Value.CancellationRequested)));
        if (plan is null || !_activeOrders.TryGetValue(plan.TrackedOrderRef, out var trackedOrder))
        {
            await SubmitOrderAsync(request, context).ConfigureAwait(true);
            return;
        }

        PlacementLifecycle = new OrderPlacementLifecycle.AwaitingTrackedCancel(
            plan.PendingOrder, plan.TrackedOrderRef, DeferredReplacementCause.BModeCloseCapacity);
        _deferredReplacementContext = context;
        if (await RequestCancelAsync(plan.TrackedOrderRef, trackedOrder, suppressStatusOnSuccess: true).ConfigureAwait(true))
        {
            StatusMessage = "B 模式平仓：等待容量替换撤单回报";
            return;
        }

        PlacementLifecycle = new OrderPlacementLifecycle.Ready();
        _deferredReplacementContext = null;
        StatusMessage = "B 模式平仓替换未提交：无法发起目标撤单";
    }

    private async Task<bool> SubmitOrderAsync(OrderRequest request, OrderValidationContext context)
    {
        var (allowed, reason) = _validator.Validate(request, context);
        if (!allowed)
        {
            StatusMessage = reason ?? "校验拒绝";
            _logger.LogWarning(
                "报单被校验链拒绝：{Reason}（{Instrument} {Dir} {Offset} {Vol}@{Price}）",
                reason, InstrumentCode, request.Direction, request.OffsetFlag, request.Volume, request.Price);
            return false;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "报单提交中…";
            var orderRef = await _trading.SendOrderAsync(request).ConfigureAwait(true);
            var active = new ActiveOrder(
                FrontId: 0,
                SessionId: 0,
                Price: request.Price,
                Direction: request.Direction,
                OffsetFlag: request.OffsetFlag,
                OriginalVolume: request.Volume,
                RemainingVolume: request.Volume,
                Sequence: Interlocked.Increment(ref _nextOrderSequence));
            if (_activeOrders.TryAdd(orderRef, active))
                NotifyActiveOrderChanged(active, isActive: true);

            StatusMessage = $"报单已提交：{orderRef}";
            _logger.LogInformation(
                "报单提交：{Instrument} {Dir} {Offset} {Vol}@{Price} Ref={Ref}",
                InstrumentCode, request.Direction, request.OffsetFlag, request.Volume, request.Price, orderRef);
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"报单失败：{ex.Message}";
            _logger.LogError(ex, "报单提交失败 {Instrument}", InstrumentCode);
            return false;
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    private async Task CancelLastOrderAsync()
    {
        if (_disposed || _activeOrders.IsEmpty) return;
        var latest = _activeOrders.OrderBy(pair => pair.Value.Sequence).LastOrDefault();
        if (string.IsNullOrEmpty(latest.Key)) return;
        await RequestCancelAsync(latest.Key, latest.Value).ConfigureAwait(true);
    }

    /// <summary>撤当前合约的所有活动订单，最终状态仍以订单回报为准。</summary>
    public async Task CancelAllOrdersAsync()
    {
        if (_disposed || _activeOrders.IsEmpty) return;
        var snapshot = _activeOrders.ToArray();
        var requested = 0;
        var failed = 0;
        foreach (var (orderRef, order) in snapshot)
        {
            if (await RequestCancelAsync(orderRef, order, suppressStatusOnSuccess: true).ConfigureAwait(true))
                requested++;
            else
                failed++;
        }

        StatusMessage = failed == 0
            ? $"撤单请求已提交：{requested} 笔"
            : $"撤单请求：已提交 {requested} 笔，未提交 {failed} 笔";
    }

    /// <summary>按合约和价格容差撤当前价位的所有活动订单。</summary>
    public async Task CancelOrdersAtPriceAsync(decimal price)
    {
        if (_disposed || _activeOrders.IsEmpty) return;
        var tolerance = _priceTick / 2m;
        var targets = _activeOrders
            .Where(pair => Math.Abs(pair.Value.Price - price) < tolerance)
            .ToArray();
        if (targets.Length == 0) return;

        var requested = 0;
        var failed = 0;
        foreach (var (orderRef, order) in targets)
        {
            if (await RequestCancelAsync(orderRef, order, suppressStatusOnSuccess: true).ConfigureAwait(true))
                requested++;
            else
                failed++;
        }

        StatusMessage = failed == 0
            ? $"撤单请求已提交：{requested} 笔（@ {price}）"
            : $"按价撤单：已提交 {requested} 笔，未提交 {failed} 笔";
    }

    private async Task<bool> RequestCancelAsync(
        string orderRef,
        ActiveOrder order,
        bool suppressStatusOnSuccess = false)
    {
        if (order.CancellationRequested) return true;
        if (order.FrontId == 0 || order.SessionId == 0)
        {
            _logger.LogDebug("跳过早撤单（报单尚未确认）：Ref={Ref}", orderRef);
            return false;
        }

        var (allowed, reason) = _risk.CheckCancel(InstrumentCode, _risk.CurrentCounters);
        if (!allowed)
        {
            StatusMessage = reason ?? "本地风控拒绝撤单";
            _logger.LogWarning("撤单被本地风控拒绝：{Reason}（Ref={Ref}）", reason, orderRef);
            return false;
        }

        try
        {
            _activeOrders.TryUpdate(orderRef, order with { CancellationRequested = true }, order);
            await _trading.CancelOrderAsync(orderRef, order.FrontId, order.SessionId).ConfigureAwait(true);
            _risk.RecordCancel(InstrumentCode);
            if (!suppressStatusOnSuccess)
                StatusMessage = $"撤单请求已提交：{orderRef}";
            _logger.LogInformation("撤单请求已提交：Ref={Ref} Front={Front} Session={Session}",
                orderRef, order.FrontId, order.SessionId);
            return true;
        }
        catch (Exception ex)
        {
            _activeOrders.TryUpdate(orderRef, order, order with { CancellationRequested = true });
            StatusMessage = $"撤单请求失败：{ex.Message}";
            _logger.LogError(ex, "撤单请求失败 Ref={Ref}", orderRef);
            return false;
        }
        finally
        {
            RefreshCommands();
        }
    }

    private void OnOrderResult(OrderResult result)
    {
        if (_disposed) return;
        if (!string.IsNullOrEmpty(result.InstrumentId)
            && !string.Equals(result.InstrumentId, InstrumentCode, StringComparison.Ordinal))
            return;

        MarshalToUi(() => ApplyOrderResult(result));
    }

    private void ApplyOrderResult(OrderResult result)
    {
        if (_disposed) return;
        var trackedReplacement = PlacementLifecycle as OrderPlacementLifecycle.AwaitingTrackedCancel;

        switch (result.Status)
        {
            case OrderStatus.Accepted:
                UpsertAcceptedOrder(result);
                StatusMessage = $"报单已接受：{result.OrderRef}";
                break;
            case OrderStatus.PartiallyFilled partial:
                UpdateActiveOrderRemainingVolume(result);
                StatusMessage = $"部分成交：{result.OrderRef}（{partial.FilledVolume}/{result.Volume}手）";
                break;
            case OrderStatus.Filled filled:
                RemoveActiveOrder(result.OrderRef);
                StatusMessage = $"全部成交：{result.OrderRef}（{filled.FilledVolume}手）";
                AbortTrackedReplacementIfNeeded(trackedReplacement, result.OrderRef, "被跟踪订单已成交");
                break;
            case OrderStatus.Canceling:
                StatusMessage = $"撤单中：{result.OrderRef}";
                break;
            case OrderStatus.Canceled canceled:
                RemoveActiveOrder(result.OrderRef);
                StatusMessage = $"已撤单：{result.OrderRef}（成交{canceled.FilledVolume}手）";
                if (trackedReplacement?.TrackedOrderRef == result.OrderRef)
                    _ = SubmitDeferredReplacementAsync(trackedReplacement);
                break;
            case OrderStatus.Rejected rejected:
                RemoveActiveOrder(result.OrderRef);
                StatusMessage = $"报单被拒：{rejected.Reason}";
                AbortTrackedReplacementIfNeeded(trackedReplacement, result.OrderRef, "被跟踪订单被拒");
                break;
            default:
                StatusMessage = $"报单状态：{result.Status}（{result.OrderRef}）";
                break;
        }
        RefreshCommands();
    }

    private async Task SubmitDeferredReplacementAsync(OrderPlacementLifecycle.AwaitingTrackedCancel replacement)
    {
        await _placementGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!Equals(PlacementLifecycle, replacement)) return;
            PlacementLifecycle = new OrderPlacementLifecycle.Ready();
            var context = _deferredReplacementContext ?? new OrderValidationContext
            {
                Now = DateTime.Now,
                ActiveOrderCount = ActiveOrderCount,
                CurrentPositionCount = CurrentPositionCount
            };
            _deferredReplacementContext = null;
            await SubmitOrderAsync(replacement.PendingOrder, context with
            {
                Now = DateTime.Now,
                ActiveOrderCount = ActiveOrderCount,
                CurrentPositionCount = CurrentPositionCount
            }).ConfigureAwait(true);
        }
        finally
        {
            _placementGate.Release();
        }
    }

    private void AbortTrackedReplacementIfNeeded(
        OrderPlacementLifecycle.AwaitingTrackedCancel? replacement,
        string orderRef,
        string reason)
    {
        if (replacement?.TrackedOrderRef != orderRef) return;
        PlacementLifecycle = new OrderPlacementLifecycle.Ready();
        _deferredReplacementContext = null;
        var prefix = replacement.Cause == DeferredReplacementCause.AModeSameDirection
            ? "A 模式替换"
            : "B 模式平仓替换";
        StatusMessage = $"{prefix}未提交：{reason}";
    }

    private void UpsertAcceptedOrder(OrderResult result)
    {
        if (_activeOrders.TryGetValue(result.OrderRef, out var existing))
        {
            var updated = existing with
            {
                FrontId = result.FrontId,
                SessionId = result.SessionId,
                RemainingVolume = ResolveRemainingVolume(result, existing.RemainingVolume)
            };
            _activeOrders[result.OrderRef] = updated;
            NotifyActiveOrderChanged(updated, isActive: true);
            return;
        }

        var active = new ActiveOrder(
            result.FrontId,
            result.SessionId,
            result.Price,
            result.Direction,
            result.OffsetFlag,
            result.Volume,
            ResolveRemainingVolume(result, result.Volume),
            Interlocked.Increment(ref _nextOrderSequence));
        _activeOrders[result.OrderRef] = active;
        NotifyActiveOrderChanged(active, isActive: true);
    }

    private void RemoveActiveOrder(string orderRef)
    {
        if (_activeOrders.TryRemove(orderRef, out var removed))
            NotifyActiveOrderChanged(removed, isActive: false);
    }

    private void UpdateActiveOrderRemainingVolume(OrderResult result)
    {
        if (!_activeOrders.TryGetValue(result.OrderRef, out var existing)) return;
        var updated = existing with { RemainingVolume = ResolveRemainingVolume(result, existing.RemainingVolume) };
        if (_activeOrders.TryUpdate(result.OrderRef, updated, existing))
            NotifyActiveOrderChanged(updated, isActive: true);
    }

    private static int ResolveRemainingVolume(OrderResult result, int fallbackVolume)
    {
        // CTP OnRtnOrder 的 VolumeTotal 是权威剩余量。测试假件和部分旧回报可能未填它（默认 0），
        // 因而仅在其有值时优先使用；若已成交量存在则仍可由原始量可靠推导。
        if (result.VolumeRemaining > 0)
            return result.VolumeRemaining;
        if (result.Volume > 0 && result.VolumeTraded > 0)
            return Math.Max(0, result.Volume - result.VolumeTraded);
        return Math.Max(0, fallbackVolume);
    }

    private void NotifyActiveOrderChanged(ActiveOrder order, bool isActive) =>
        ActiveOrdersChanged?.Invoke(this, new OrderActiveStateChangedEventArgs(
            order.Direction, order.OffsetFlag, order.Price, isActive));

    private void OnPositionUpdate(Position position)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            switch (position.Direction)
            {
                case Direction.Buy:
                    _longTodayPosition = position.TodayPosition;
                    _longYesterdayPosition = position.YdPosition;
                    _longFrozenPosition = position.FrozenPosition;
                    break;
                case Direction.Sell:
                    _shortTodayPosition = position.TodayPosition;
                    _shortYesterdayPosition = position.YdPosition;
                    _shortFrozenPosition = position.FrozenPosition;
                    break;
            }
        });
    }

    private OppositePosition GetOppositePosition(Direction direction) => direction switch
    {
        Direction.Buy => new OppositePosition(_shortTodayPosition, _shortYesterdayPosition, _shortFrozenPosition),
        Direction.Sell => new OppositePosition(_longTodayPosition, _longYesterdayPosition, _longFrozenPosition),
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "未知买卖方向")
    };

    private DateTime? GetLastMarketUpdate(PriceLadderTradeSide side) => side switch
    {
        PriceLadderTradeSide.FirstTradeColumn => _firstTradeSideMarketUpdate,
        PriceLadderTradeSide.SecondTradeColumn => _secondTradeSideMarketUpdate,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "不是可交易的价格梯侧")
    };

    private int CurrentPositionCount => Math.Max(0, _longTodayPosition + _longYesterdayPosition)
        + Math.Max(0, _shortTodayPosition + _shortYesterdayPosition);

    private bool CanSendOrder() => !IsBusy && !_disposed && Quantity > 0 && Price > 0;

    private bool CanCancelOrder() => !IsBusy && !_disposed && !_activeOrders.IsEmpty;

    private void RefreshCommands()
    {
        if (_disposed) return;
        OrderCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveOrderCount));
    }

    private void OnStreamError(Exception ex) => _logger.LogError(ex, "OrderStream 出错 {Instrument}", InstrumentCode);

    private static void MarshalToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscriptions.Dispose();
        _activeOrders.Clear();
        _placementGate.Dispose();
    }
}

/// <summary>活动订单的只读投影，供价格梯显示和 A/B 策略审计。</summary>
public sealed record ActiveOrderInfo(
    Direction Direction,
    OffsetFlag OffsetFlag,
    decimal Price,
    int OriginalVolume,
    int RemainingVolume,
    bool CancellationRequested);

/// <summary>活动订单改变时的最小负载。</summary>
public sealed class OrderActiveStateChangedEventArgs : EventArgs
{
    public OrderActiveStateChangedEventArgs(Direction direction, OffsetFlag offsetFlag, decimal price, bool isActive)
    {
        Direction = direction;
        OffsetFlag = offsetFlag;
        Price = price;
        IsActive = isActive;
    }

    public Direction Direction { get; }
    public OffsetFlag OffsetFlag { get; }
    public decimal Price { get; }
    public bool IsActive { get; }
}
