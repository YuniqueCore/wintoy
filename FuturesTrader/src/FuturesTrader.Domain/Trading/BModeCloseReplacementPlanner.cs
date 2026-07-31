namespace FuturesTrader.Domain.Trading;

/// <summary>
/// B 模式平仓路径中的活动单快照。旧程序按当前未成交量统计平今/平昨挂单，
/// 并在平仓挂单总量恰好覆盖反向持仓时，仅撤一笔非开仓单后异步替换。
/// </summary>
public sealed record BModeActiveOrder(
    string OrderRef,
    Direction Direction,
    OffsetFlag OffsetFlag,
    int RemainingVolume,
    long Sequence,
    bool CancellationRequested);

/// <summary>B 模式平仓容量已满时的单笔替换计划。</summary>
public sealed record BModeCloseReplacementPlan(string TrackedOrderRef, OrderRequest PendingOrder);

/// <summary>
/// 复刻 sub_4C77B4 / sub_4C9144 中已证实的 B 平仓容量分支。
/// 它只决定是否需要等待一笔已有平仓单撤回，不负责发送撤单或提交报单。
/// </summary>
public static class BModeCloseReplacementPlanner
{
    public static BModeCloseReplacementPlan? TryPlan(
        bool onlyOpen,
        OrderRequest requestedOrder,
        OppositePosition oppositePosition,
        IEnumerable<BModeActiveOrder> activeOrders)
    {
        ArgumentNullException.ThrowIfNull(requestedOrder);
        ArgumentNullException.ThrowIfNull(oppositePosition);
        ArgumentNullException.ThrowIfNull(activeOrders);

        if (onlyOpen || requestedOrder.OffsetFlag == OffsetFlag.Open)
            return null;

        var sameDirection = activeOrders
            .Where(order => order.Direction == requestedOrder.Direction && order.RemainingVolume > 0)
            .OrderBy(order => order.Sequence)
            .ToArray();
        var closeTodayVolume = sameDirection
            .Where(order => order.OffsetFlag == OffsetFlag.CloseToday)
            .Sum(order => order.RemainingVolume);
        var closeYesterdayVolume = sameDirection
            .Where(order => order.OffsetFlag == OffsetFlag.CloseYesterday)
            .Sum(order => order.RemainingVolume);
        var positionVolume = Math.Max(0, oppositePosition.TodayPosition)
            + Math.Max(0, oppositePosition.YesterdayPosition);

        // 旧程序只在平今 + 平昨活动量恰好等于反向持仓时进入单笔替换。
        if (closeTodayVolume + closeYesterdayVolume != positionVolume)
            return null;

        var trackedOrder = sameDirection.FirstOrDefault(order =>
            !order.CancellationRequested
            && order.OffsetFlag is OffsetFlag.CloseToday or OffsetFlag.CloseYesterday);
        if (trackedOrder is null)
            return null;

        var offsetCapacity = trackedOrder.OffsetFlag == OffsetFlag.CloseToday
            ? Math.Max(0, oppositePosition.TodayPosition - closeTodayVolume + trackedOrder.RemainingVolume)
            : Math.Max(0, oppositePosition.YesterdayPosition - closeYesterdayVolume + trackedOrder.RemainingVolume);
        var replacementVolume = Math.Min(requestedOrder.Volume, offsetCapacity);
        if (replacementVolume <= 0)
            return null;

        return new BModeCloseReplacementPlan(
            trackedOrder.OrderRef,
            requestedOrder with
            {
                OffsetFlag = trackedOrder.OffsetFlag,
                Volume = replacementVolume
            });
    }
}
