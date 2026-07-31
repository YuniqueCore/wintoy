namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 鼠标用于选择挂单数量的按键。它与买卖方向完全无关。
/// </summary>
public enum MouseQuantityButton
{
    Left,
    Right
}

/// <summary>
/// 价格梯中两个可交易的物理侧。旧程序只让第 1、3 列进入下单入口；
/// 此类型故意不叫 Buy/Sell，避免把物理位置写死成方向。
/// </summary>
public enum PriceLadderTradeSide
{
    FirstTradeColumn = 1,
    SecondTradeColumn = 3
}

/// <summary>
/// 点价下单的挂单策略。A 是同合约同方向替换，B 是普通追加。
/// </summary>
public enum OrderPlacementMode
{
    ReplaceSameDirection,
    Append
}

/// <summary>异步撤单完成后提交替换单的来源。</summary>
public enum DeferredReplacementCause
{
    AModeSameDirection,
    BModeCloseCapacity
}

/// <summary>
/// 将价格梯物理交易侧映射为 CTP 买卖方向。
/// 默认第一个交易列为 Buy、第二个交易列为 Sell；某些合约会反转这条路由。
/// </summary>
public readonly record struct PriceLadderDirectionMap(bool IsInverted = false)
{
    public Direction Resolve(PriceLadderTradeSide side) => (side, IsInverted) switch
    {
        (PriceLadderTradeSide.FirstTradeColumn, false) => Direction.Buy,
        (PriceLadderTradeSide.SecondTradeColumn, false) => Direction.Sell,
        (PriceLadderTradeSide.FirstTradeColumn, true) => Direction.Sell,
        (PriceLadderTradeSide.SecondTradeColumn, true) => Direction.Buy,
        _ => throw new ArgumentOutOfRangeException(nameof(side), side, "不是可交易的价格梯侧")
    };
}

/// <summary>
/// 价格梯替换单的显式生命周期。A 模式会追踪最后一个同方向旧单；
/// B 平仓容量分支只追踪被选中的一笔平仓单。两者都只在对应 Canceled 回报后提交暂存新单。
/// </summary>
public abstract record OrderPlacementLifecycle
{
    private OrderPlacementLifecycle() { }

    public sealed record Ready : OrderPlacementLifecycle;

    public sealed record AwaitingTrackedCancel(
        OrderRequest PendingOrder,
        string TrackedOrderRef,
        DeferredReplacementCause Cause = DeferredReplacementCause.AModeSameDirection)
        : OrderPlacementLifecycle;
}
