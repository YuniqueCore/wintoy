using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 下单校验抽象：对齐 0527.exe <c>sub_4C036C</c>（@ 0x4C036C，核心下单函数）的 7 步校验链。
/// <para>
/// 在价格 ladder 点击下单时，依次执行以下校验（任一失败即拒绝）：
/// <list type="number">
///   <item><b>合约存在</b>：InstrumentCode 非空且合约元数据已加载。</item>
///   <item><b>交易时段</b>：委托 <see cref="ITradingSessionChecker"/>，非交易时段拒单。</item>
///   <item><b>仅平仓校验</b>：CBOnlyOpen 勾选时，仅允许平仓方向（CloseToday/CloseYesterday）。</item>
///   <item><b>CBNearby 节流</b>：同方向点击间隔 &lt; 阈值 ms 拒单（提示 "Chg Nearby!"）。</item>
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

    /// <summary>
    /// 记录一次点击时刻（用于 CBNearby 节流）。
    /// 在 <see cref="Validate"/> 通过后、实际下单前调用。
    /// </summary>
    /// <param name="direction">点击方向（Buy=左键 / Sell=右键）。</param>
    /// <param name="clickTime">点击时刻。</param>
    void RecordClick(Direction direction, DateTime clickTime);
}

/// <summary>
/// 下单校验上下文：聚合校验所需的外部状态（由调用方在点击时填充）。
/// 对齐 0527.exe TPointWindow 对象中的相关字段偏移。
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
    /// CBNearby 节流是否启用（TPointWindow +1140）。
    /// 启用时同方向点击间隔需 ≥ <see cref="NearbyThrottleMs"/>，否则拒单。
    /// </summary>
    public bool NearbyEnabled { get; init; }

    /// <summary>CBNearby 节流阈值（毫秒，来自主窗 +2244 配置）。</summary>
    public int NearbyThrottleMs { get; init; } = 500;

    /// <summary>
    /// CBOnlyOpen 是否启用（TPointWindow +1144）。
    /// 启用时仅允许平仓方向（CloseToday/CloseYesterday/Close）。
    /// </summary>
    public bool OnlyOpenEnabled { get; init; }

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
