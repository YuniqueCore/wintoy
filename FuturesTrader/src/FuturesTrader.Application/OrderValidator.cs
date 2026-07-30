using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application;

/// <summary>
/// 下单校验默认实现：对齐 0527.exe <c>sub_4C036C</c>（@ 0x4C036C）的 7 步校验链。
/// <para>
/// 校验顺序（任一失败即拒绝，返回原因）：
/// <list type="number">
///   <item>合约存在：<see cref="OrderRequest.InstrumentId"/> 非空。</item>
///   <item>交易时段：委托 <see cref="ITradingSessionChecker.CheckOrderAllowed"/>。</item>
///   <item>仅平仓（CBOnlyOpen）：勾选时拒绝开仓方向。</item>
///   <item>CBNearby 节流：同方向点击间隔 &lt; 阈值拒单（"Chg Nearby!"）。</item>
///   <item>对手价（CBMorderX）：勾选时校验对手价有效。</item>
///   <item>本地风控：委托 <see cref="ILocalRiskService.CheckOrder"/>。</item>
///   <item>价格 tick：价格为 PriceTick 整数倍。</item>
/// </list>
/// </para>
/// <para>
/// <b>线程安全</b>：<see cref="Validate"/> 与 <see cref="RecordClick"/> 设计为 UI 线程调用（点击事件），
/// 内部用 <see cref="Interlocked"/> 保护点击时刻记录，防止 CTP 回调线程竞争。
/// </para>
/// </summary>
public sealed class OrderValidator : IOrderValidator
{
    private readonly ITradingSessionChecker _sessionChecker;
    private readonly ILocalRiskService _riskService;
    private long _lastBuyClickTicks;
    private long _lastSellClickTicks;

    public OrderValidator(ITradingSessionChecker sessionChecker, ILocalRiskService riskService)
    {
        _sessionChecker = sessionChecker ?? throw new ArgumentNullException(nameof(sessionChecker));
        _riskService = riskService ?? throw new ArgumentNullException(nameof(riskService));
    }

    /// <inheritdoc />
    public (bool Allowed, string? Reason) Validate(OrderRequest request, OrderValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        // 步骤 1：合约存在校验
        if (string.IsNullOrWhiteSpace(request.InstrumentId))
            return (false, "合约代码为空");

        // 步骤 2：交易时段校验（sub_4DCBC0）
        var (sessionAllowed, sessionReason) = _sessionChecker.CheckOrderAllowed(context.Now);
        if (!sessionAllowed)
            return (false, sessionReason ?? "非交易时段");

        // 步骤 3：仅平仓校验（CBOnlyOpen，+1144）
        if (context.OnlyOpenEnabled && request.OffsetFlag == OffsetFlag.Open)
            return (false, "CBOnlyOpen 已启用，仅允许平仓方向");

        // 步骤 4：CBNearby 节流（同方向点击间隔 < 阈值 → "Chg Nearby!"）
        if (context.NearbyEnabled && context.NearbyThrottleMs > 0)
        {
            var lastClick = GetLastClickTicks(request.Direction);
            if (lastClick > 0)
            {
                var lastClickTime = new DateTime(lastClick, DateTimeKind.Local);
                var elapsedMs = (long)(context.Now - lastClickTime).TotalMilliseconds;
                if (elapsedMs < context.NearbyThrottleMs)
                    return (false, "Chg Nearby!");
            }
        }

        // 步骤 5：对手价模式（CBMorderX）校验
        if (context.UseOpponentPrice)
        {
            if (!context.OpponentPrice.HasValue || context.OpponentPrice.Value <= 0)
                return (false, "CBMorderX 已启用但对手价无效");
        }
        else if (request.Price <= 0)
        {
            return (false, "价格必须 > 0");
        }

        // 步骤 6：本地风控校验（报单数/持仓数限制）
        var (riskAllowed, riskReason) = _riskService.CheckOrder(
            request, context.CurrentOrderCount, context.CurrentPositionCount);
        if (!riskAllowed)
            return (false, riskReason ?? "本地风控拒绝");

        // 步骤 7：价格 tick 校验（PriceTick=0 时跳过，兼容市价/对手价）
        var effectivePrice = context.UseOpponentPrice ? context.OpponentPrice!.Value : request.Price;
        if (request.PriceTick > 0 && effectivePrice > 0 && effectivePrice % request.PriceTick != 0)
            return (false, $"价格必须是 {request.PriceTick} 的整数倍");

        return (true, null);
    }

    /// <inheritdoc />
    public void RecordClick(Direction direction, DateTime clickTime)
    {
        var ticks = clickTime.Ticks;
        switch (direction)
        {
            case Direction.Buy:
                Interlocked.Exchange(ref _lastBuyClickTicks, ticks);
                break;
            case Direction.Sell:
                Interlocked.Exchange(ref _lastSellClickTicks, ticks);
                break;
        }
    }

    private long GetLastClickTicks(Direction direction) =>
        direction switch
        {
            Direction.Buy => Interlocked.Read(ref _lastBuyClickTicks),
            Direction.Sell => Interlocked.Read(ref _lastSellClickTicks),
            _ => 0
        };
}
