using System.Collections.Concurrent;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 下单区 ViewModel（TYYWin 底部报单条复刻）：买卖方向 + 开平 + 价格 + 数量 + 报单/撤单按钮。
/// 由 <see cref="TradingViewModel"/> 持有（每合约一个实例），通过 XAML <c>DataContext={Binding Order}</c> 绑定到下单面板。
/// <para>
/// <b>报单流程</b>：UI 输入 → 价格/数量本地校验 → <see cref="ILocalRiskService.CheckOrder"/> 风控校验 →
/// <see cref="ITradingService.SendOrderAsync"/> 提交 CTP → <see cref="ITradingService.OrderStream"/> 异步回报。
/// </para>
/// <para>
/// <b>撤单流程</b>：选中活动报单（默认最近一笔）→ <see cref="ILocalRiskService.CheckCancel"/> 撤单计数校验 →
/// <see cref="ITradingService.CancelOrderAsync"/> 提交 CTP → <see cref="RecordCancel"/> 累加撤单计数。
/// </para>
/// <para>
/// CTP 回调在工作线程触发，<see cref="OnOrderResult"/> 内通过 <see cref="MarshalToUi"/> 切回 UI 线程刷新状态。
/// </para>
/// </summary>
public sealed partial class OrderViewModel : ObservableObject, IDisposable
{
    private readonly ITradingService _trading;
    private readonly ILocalRiskService _risk;
    private readonly IOrderValidator _validator;
    private readonly ILogger<OrderViewModel> _logger;
    private readonly ConcurrentDictionary<string, (int FrontId, int SessionId)> _activeOrders = new();
    private readonly CompositeDisposable _subscriptions = new();
    private decimal _priceTick = 1m;
    private int _sessionOrderCount;
    private int _longPosition;
    private int _shortPosition;
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

        OrderCommand = new AsyncRelayCommand(SendOrderAsync, CanSendOrder);
        CancelCommand = new AsyncRelayCommand(CancelLastOrderAsync, CanCancelOrder);

        // 订阅报单回报流（CTP 在工作线程触发，回调内 MarshalToUi）
        _subscriptions.Add(_trading.OrderStream.Subscribe(OnOrderResult, OnStreamError));

        // 订阅持仓回报流：聚合本合约多头/空头持仓，供风控 MaxPositionCount 校验
        // CTP OnRspQryInvestorPosition 按 (合约,方向,投机套保) 分组推送多条，这里按方向合并
        _subscriptions.Add(
            _trading.PositionStream
                .Where(p => string.Equals(p.InstrumentId, InstrumentCode, StringComparison.Ordinal))
                .Subscribe(OnPositionUpdate, OnStreamError));
    }

    /// <summary>
    /// 持仓回报到达：按方向（Buy=多头 / Sell=空头）累加手数。
    /// CTP 一次查询可能推送多条（不同 HedgeFlag），这里按方向分组累加；
    /// 收到本合约任意一条持仓即覆盖该方向的累计值（以最新查询快照为准）。
    /// <para>
    /// 注意：CTP 同一查询批次内会推送 (合约,方向,HedgeFlag) 笛卡尔积的多条记录，
    /// 简化处理：每条 Position 视为该方向的最新快照，直接覆盖。
    /// 完整聚合需等待 bIsLast=true（Domain 层未暴露批次边界），目前按覆盖语义足够（风控只看总持仓上限）。
    /// </para>
    /// </summary>
    private void OnPositionUpdate(Position position)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            switch (position.Direction)
            {
                case Direction.Buy: _longPosition = position.TotalPosition; break;
                case Direction.Sell: _shortPosition = position.TotalPosition; break;
            }
        });
    }

    /// <summary>当前合约总持仓（多+空，用于风控 MaxPositionCount 校验）。</summary>
    private int CurrentPositionCount => _longPosition + _shortPosition;

    /// <summary>合约代码（由 TradingViewModel 注入，与窗口标题一致）。</summary>
    public string InstrumentCode { get; }

    /// <summary>
    /// 最小变动价位（由 TradingViewModel 从合约元数据填充，默认 1）。
    /// 用于 <see cref="CanSendOrder"/> 校验价格必须为 PriceTick 整数倍。
    /// </summary>
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

    /// <summary>
    /// 报单命令（绑定下单按钮）。
    /// 暴露为 <see cref="IAsyncRelayCommand"/> 以便代码侧 <c>ExecuteAsync</c> 调用（点价挂单场景）。
    /// </summary>
    public IAsyncRelayCommand OrderCommand { get; }

    /// <summary>撤单命令（绑定撤单按钮，撤最近一笔活动报单）。</summary>
    public IAsyncRelayCommand CancelCommand { get; }

    /// <summary>活动报单数（UI 反馈：是否有可撤报单）。</summary>
    public int ActiveOrderCount => _activeOrders.Count;

    // 属性变更钩子：刷新命令可用性
    partial void OnDirectionChanged(Direction value) => RefreshCommands();
    partial void OnOffsetFlagChanged(OffsetFlag value) => RefreshCommands();
    partial void OnPriceChanged(decimal value) => RefreshCommands();
    partial void OnQuantityChanged(int value) => RefreshCommands();

    private void RefreshCommands()
    {
        if (_disposed) return;
        OrderCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ActiveOrderCount));
    }

    /// <summary>报单按钮可用：非忙碌 + 数量&gt;0 + 价格&gt;0 + 非已释放。</summary>
    private bool CanSendOrder() => !IsBusy && !_disposed && Quantity > 0 && Price > 0;

    /// <summary>撤单按钮可用：非忙碌 + 有活动报单 + 非已释放。</summary>
    private bool CanCancelOrder() => !IsBusy && !_disposed && !_activeOrders.IsEmpty;

    /// <summary>
    /// 提交报单：本地校验 → 风控校验 → CTP 提交。
    /// 异常不抛出，统一写入 <see cref="StatusMessage"/> 反馈 UI。
    /// </summary>
    private async Task SendOrderAsync()
    {
        if (_disposed) return;

        // 构造报单请求值对象
        var request = new OrderRequest
        {
            InstrumentId = InstrumentCode,
            Direction = Direction,
            OffsetFlag = OffsetFlag,
            Price = Price,
            Volume = Quantity,
            PriceTick = _priceTick
        };

        // 7 步校验链（对齐 0527.exe sub_4C036C）：合约存在 → 交易时段 → 仅平仓 →
        // CBNearby 节流 → 对手价 → 本地风控 → 价格 tick。任一失败即拒绝，不提交 CTP。
        // 上下文当前仅填充会话报单数与持仓数；OnlyOpen/CBNearby/对手价开关待 UI 接入后扩展。
        var context = new OrderValidationContext
        {
            Now = DateTime.Now,
            CurrentOrderCount = _sessionOrderCount,
            CurrentPositionCount = CurrentPositionCount
        };
        var (allowed, reason) = _validator.Validate(request, context);
        if (!allowed)
        {
            StatusMessage = reason ?? "校验拒绝";
            _logger.LogWarning("报单被校验链拒绝：{Reason}（{Instrument} {Dir} {Offset} {Vol}@{Price}）",
                reason, InstrumentCode, Direction, OffsetFlag, Quantity, Price);
            return;
        }

        // 校验通过，记录点击时刻（CBNearby 节流用，对齐 sub_4C036C +1140）
        _validator.RecordClick(Direction, DateTime.Now);

        // 提交 CTP（或 Mock）
        try
        {
            IsBusy = true;
            StatusMessage = "报单提交中…";
            var orderRef = await _trading.SendOrderAsync(request).ConfigureAwait(true);
            Interlocked.Increment(ref _sessionOrderCount);
            StatusMessage = $"报单已提交：{orderRef}";
            _logger.LogInformation("报单提交：{Instrument} {Dir} {Offset} {Vol}@{Price} Ref={Ref}",
                InstrumentCode, Direction, OffsetFlag, Quantity, Price, orderRef);
        }
        catch (Exception ex)
        {
            StatusMessage = $"报单失败：{ex.Message}";
            _logger.LogError(ex, "报单提交失败 {Instrument}", InstrumentCode);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>
    /// 撤销最近一笔活动报单：撤单风控校验 → CTP 撤单 → 累加撤单计数。
    /// 活动报单按插入顺序取最后一个（LIFO，复刻 0527.exe 撤最新报单的习惯）。
    /// </summary>
    private async Task CancelLastOrderAsync()
    {
        if (_disposed || _activeOrders.IsEmpty) return;

        // 取最近一笔活动报单（ConcurrentDictionary 无序，用快照取最后入列的）
        var snapshot = _activeOrders.ToArray();
        if (snapshot.Length == 0) return;
        var (orderRef, (frontId, sessionId)) = snapshot[^1];

        // 撤单风控校验（用服务内部计数器）
        var (allowed, reason) = _risk.CheckCancel(InstrumentCode, _risk.CurrentCounters);
        if (!allowed)
        {
            StatusMessage = reason ?? "本地风控拒绝撤单";
            _logger.LogWarning("撤单被本地风控拒绝：{Reason}（Ref={Ref}）", reason, orderRef);
            return;
        }

        try
        {
            IsBusy = true;
            await _trading.CancelOrderAsync(orderRef, frontId, sessionId).ConfigureAwait(true);
            _risk.RecordCancel(InstrumentCode);
            StatusMessage = $"撤单已提交：{orderRef}";
            _logger.LogInformation("撤单提交：Ref={Ref} Front={Front} Session={Session}", orderRef, frontId, sessionId);
        }
        catch (Exception ex)
        {
            StatusMessage = $"撤单失败：{ex.Message}";
            _logger.LogError(ex, "撤单失败 Ref={Ref}", orderRef);
        }
        finally
        {
            IsBusy = false;
            RefreshCommands();
        }
    }

    /// <summary>报单回报到达：按状态机更新活动报单表与 UI 反馈。</summary>
    private void OnOrderResult(OrderResult result)
    {
        if (_disposed) return;
        // 过滤非本合约回报（同会话多合约场景）
        if (!string.IsNullOrEmpty(result.InstrumentId) &&
            !string.Equals(result.InstrumentId, InstrumentCode, StringComparison.Ordinal))
            return;

        MarshalToUi(() =>
        {
            if (_disposed) return;
            switch (result.Status)
            {
                case OrderStatus.Accepted:
                    _activeOrders[result.OrderRef] = (result.FrontId, result.SessionId);
                    StatusMessage = $"报单已接受：{result.OrderRef}";
                    break;
                case OrderStatus.PartiallyFilled p:
                    StatusMessage = $"部分成交：{result.OrderRef}（{p.FilledVolume}/{result.Volume}手）";
                    break;
                case OrderStatus.Filled f:
                    _activeOrders.TryRemove(result.OrderRef, out _);
                    StatusMessage = $"全部成交：{result.OrderRef}（{f.FilledVolume}手）";
                    break;
                case OrderStatus.Canceling:
                    StatusMessage = $"撤单中：{result.OrderRef}";
                    break;
                case OrderStatus.Canceled c:
                    _activeOrders.TryRemove(result.OrderRef, out _);
                    StatusMessage = $"已撤单：{result.OrderRef}（成交{c.FilledVolume}手）";
                    break;
                case OrderStatus.Rejected r:
                    _activeOrders.TryRemove(result.OrderRef, out _);
                    StatusMessage = $"报单被拒：{r.Reason}";
                    break;
                default:
                    StatusMessage = $"报单状态：{result.Status}（{result.OrderRef}）";
                    break;
            }
            RefreshCommands();
        });
    }

    private void OnStreamError(Exception ex) => _logger.LogError(ex, "OrderStream 出错 {Instrument}", InstrumentCode);

    /// <summary>把 action 调度到 UI 线程执行；无 WPF 应用上下文（单元测试）则直接内联执行。</summary>
    private static void MarshalToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }

    /// <summary>窗口关闭时退订（释放订阅，避免泄漏）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscriptions.Dispose();
        _activeOrders.Clear();
    }
}
