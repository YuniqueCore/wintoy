using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 下单校验抽象：对齐 0527.exe <c>sub_4C036C</c>（@ 0x4C036C，核心下单函数）的 7 步校验链。
/// <para>
/// 在价格 ladder 点击下单时，依次执行以下校验（任一失败即拒绝）：
/// <list type="number">
///   <item><b>合约存在</b>：InstrumentCode 非空且合约元数据已加载。</item>
///   <item><b>交易时段</b>：委托 <see cref="ITradingSessionChecker"/>，非交易时段拒单。</item>
///   <item><b>开平决策</b>：由调用方在构造请求前根据 CBOnlyOpen 和反向持仓完成。</item>
///   <item><b>CBNearby 保护</b>：相关交易侧最近一次行情更新距当前小于阈值时拒单（提示 "Chg Nearby!"）。</item>
///   <item><b>对手价模式</b>：CBMorderX 勾选时，以对手盘最优价为 LimitPrice（覆盖点击价）。</item>
///   <item><b>本地风控</b>：委托 <see cref="ILocalRiskService"/>，报单数/持仓数超限拒单。</item>
///   <item><b>价格 tick 校验</b>：价格必须为 PriceTick 整数倍。</item>
/// </list>
/// 校验通过后，调用方再执行 <see cref="ITradingService.SendOrderAsync"/> 提交 CTP。
/// </para>
/// </summary>
public interface IOrderValidator
{
    /// <summary>
    /// 执行 7 步校验链。返回是否允许下单及拒绝原因。
    /// </summary>
    /// <param name="request">报单请求（方向/开平/价格/数量已由 UI 填充）。</param>
    /// <param name="context">校验上下文（当前时间/持仓/上次点击时刻/开关状态等）。</param>
    /// <returns>允许返回 <c>(true, null)</c>；拒绝返回 <c>(false, 原因)</c>。</returns>
    (bool Allowed, string? Reason) Validate(OrderRequest request, OrderValidationContext context);

}

/// <summary>
/// 下单校验上下文：聚合校验所需的外部状态（由调用方在点击时填充）。
/// 对齐 0527.exe TYYWin 对象中的相关字段和运行时状态。
/// </summary>
public sealed record OrderValidationContext
{
    /// <summary>当前时间（CST，用于交易时段校验）。</summary>
    public DateTime Now { get; init; } = DateTime.Now;

    /// <summary>当前会话已提交报单总数（对应 ILocalRiskService.CheckOrder 的 currentOrderCount）。</summary>
    public int CurrentOrderCount { get; init; }

    /// <summary>当前持仓合约数（多+空，对应 ILocalRiskService.CheckOrder 的 currentPositionCount）。</summary>
    public int CurrentPositionCount { get; init; }

    /// <summary>
    /// CBNearby 行情邻近保护是否启用（TYYWin +1140）。
    /// 启用时相关交易侧自最近一次行情更新以来必须经过至少 <see cref="NearbyThrottleMs"/>。
    /// </summary>
    public bool NearbyEnabled { get; init; }

    /// <summary>CBNearby 阈值（毫秒，来自运行时配置，而非固定点击冷却）。</summary>
    public int NearbyThrottleMs { get; init; }

    /// <summary>
    /// 与本次价格梯交易侧对应的最近一次行情更新时刻。
    /// 空值表示调用方尚未观察到可用于邻近保护的更新，不做节流拒绝。
    /// </summary>
    public DateTime? LastRelevantMarketUpdate { get; init; }

    /// <summary>
    /// CBMorderX 对手价模式是否启用。
    /// 启用时以 <see cref="OpponentPrice"/> 作为 LimitPrice，覆盖 <see cref="OrderRequest.Price"/>。
    /// </summary>
    public bool UseOpponentPrice { get; init; }

    /// <summary>
    /// 对手盘最优价（CBMorderX 启用时使用）。
    /// 买方向用卖一价（AskPrice1），卖方向用买一价（BidPrice1）。
    /// </summary>
    public decimal? OpponentPrice { get; init; }
}
