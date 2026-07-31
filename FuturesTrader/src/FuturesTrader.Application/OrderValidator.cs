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
///   <item>开平决策：由调用方在构造请求前根据 CBOnlyOpen 和反向持仓完成。</item>
///   <item>CBNearby 保护：行情更新后尚未经过阈值时拒单（"Chg Nearby!"）。</item>
///   <item>对手价（CBMorderX）：勾选时校验对手价有效。</item>
///   <item>本地风控：委托 <see cref="ILocalRiskService.CheckOrder"/>。</item>
///   <item>价格 tick：价格为 PriceTick 整数倍。</item>
/// </list>
/// </para>
/// <para>
/// 此校验器是纯逻辑；行情更新时间由调用方在 <see cref="OrderValidationContext"/> 中传入，
/// 因而不会把鼠标点击时刻误当成 CBNearby 的依据。
/// </para>
/// </summary>
public sealed class OrderValidator : IOrderValidator
{
    private readonly ITradingSessionChecker _sessionChecker;
    private readonly ILocalRiskService _riskService;
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

        // 步骤 3：CBNearby 保护。旧程序比较的是行情路径维护的方向时间戳，
        // 不是上一次鼠标点击，因此这里只读取调用方传入的行情更新时间。
        if (context.NearbyEnabled && context.NearbyThrottleMs > 0 && context.LastRelevantMarketUpdate is { } lastUpdate)
        {
            var elapsedMs = (long)(context.Now - lastUpdate).TotalMilliseconds;
            if (elapsedMs >= 0 && elapsedMs < context.NearbyThrottleMs)
                return (false, "Chg Nearby!");
        }

        // 步骤 4：对手价模式（CBMorderX）校验
        if (context.UseOpponentPrice)
        {
            if (!context.OpponentPrice.HasValue || context.OpponentPrice.Value <= 0)
                return (false, "CBMorderX 已启用但对手价无效");
        }
        else if (request.Price <= 0)
        {
            return (false, "价格必须 > 0");
        }

        // 步骤 5：本地风控校验（报单数/持仓数限制）
        var (riskAllowed, riskReason) = _riskService.CheckOrder(
            request, context.CurrentOrderCount, context.CurrentPositionCount);
        if (!riskAllowed)
            return (false, riskReason ?? "本地风控拒绝");

        // 步骤 6：价格 tick 校验（PriceTick=0 时跳过，兼容市价/对手价）
        var effectivePrice = context.UseOpponentPrice ? context.OpponentPrice!.Value : request.Price;
        if (request.PriceTick > 0 && effectivePrice > 0 && effectivePrice % request.PriceTick != 0)
            return (false, $"价格必须是 {request.PriceTick} 的整数倍");

        return (true, null);
    }
}
