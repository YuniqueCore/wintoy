namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 投资者持仓值对象：CTP <c>OnRspQryInvestorPosition</c> → 映射为不可变 record 推送到 <see cref="ITradingService.PositionStream"/>。
/// CTP 按 (合约, 方向, 投机套保) 分组返回多条记录，每条对应一个持仓明细。
/// <para>
/// <b>关键字段映射</b>（CTP → Domain）：
/// <list type="bullet">
///   <item><c>InstrumentID</c> → <see cref="InstrumentId"/></item>
///   <item><c>InvestorID</c> → <see cref="InvestorId"/></item>
///   <item><c>PosiDirection</c>（'2'=Long '3'=Short）→ <see cref="Direction"/>（Buy/Sell）</item>
///   <item><c>HedgeFlag</c>（'1'/'2'/'3'）→ <see cref="HedgeFlag"/></item>
///   <item><c>TodayPosition</c> → <see cref="TodayPosition"/></item>
///   <item><c>YdPosition</c> → <see cref="YdPosition"/></item>
///   <item><c>Position</c> → <see cref="TotalPosition"/></item>
///   <item><c>FrozenPosition</c> → <see cref="FrozenPosition"/></item>
///   <item><c>PositionCost</c> → <see cref="PositionCost"/></item>
///   <item><c>PositionProfit</c> → <see cref="PositionProfit"/></item>
/// </list>
/// </para>
/// <para>
/// 浮动栏「持」字段 = 同一合约所有 <see cref="Position"/> 记录的 <see cref="TotalPosition"/> 之和（多空合计手数）。
/// 风控 <see cref="Application.Abstractions.ILocalRiskService"/> 用聚合后的总持仓判断 <c>MaxPositionCount</c>。
/// </para>
/// </summary>
public sealed record Position
{
    /// <summary>合约代码（如 ag2608）。</summary>
    public string InstrumentId { get; init; } = string.Empty;

    /// <summary>投资者代码（CTP InvestorID）。</summary>
    public string InvestorId { get; init; } = string.Empty;

    /// <summary>多空方向（CTP PosiDirection：Long→Buy 多头持仓，Short→Sell 空头持仓）。</summary>
    public Direction Direction { get; init; }

    /// <summary>投机套保标志（CTP HedgeFlag）。</summary>
    public HedgeFlag HedgeFlag { get; init; }

    /// <summary>今日持仓（今仓，CTP TodayPosition）。</summary>
    public int TodayPosition { get; init; }

    /// <summary>昨日持仓（昨仓，CTP YdPosition）。</summary>
    public int YdPosition { get; init; }

    /// <summary>总持仓手数（CTP Position = TodayPosition + YdPosition，含冻结）。</summary>
    public int TotalPosition { get; init; }

    /// <summary>冻结持仓（已挂未成交平仓的报单冻结量，CTP FrozenPosition）。</summary>
    public int FrozenPosition { get; init; }

    /// <summary>持仓成本（CTP PositionCost，开仓均价 × 手数 × 合约乘数）。</summary>
    public decimal PositionCost { get; init; }

    /// <summary>持仓盈亏（CTP PositionProfit，浮动盈亏）。</summary>
    public decimal PositionProfit { get; init; }

    /// <summary>合约乘数（CTP VolumeMultiple，用于盈亏计算）。</summary>
    public int VolumeMultiple { get; init; }
}
