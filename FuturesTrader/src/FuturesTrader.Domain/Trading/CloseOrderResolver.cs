namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 与即将发出方向相反的仓位快照。<see cref="FrozenPosition"/> 是 CTP 提供的总冻结数，
/// 但旧版点价分支按原始今/昨仓位决定平仓意图，再由活动平仓单容量分支处理替换，
/// 不能用总冻结数把该点击误改写为开仓。
/// </summary>
public sealed record OppositePosition(int TodayPosition, int YesterdayPosition, int FrozenPosition)
{
    /// <summary>原始今昨仓位总和，供旧版点价开平决策使用。</summary>
    public int LegacyCloseablePosition => Math.Max(0, TodayPosition) + Math.Max(0, YesterdayPosition);

    /// <summary>
    /// CTP 聚合可用量，仅供需要风险展示的调用方使用。它不参与旧版价格梯的开平标志选择，
    /// 因为冻结量无法按今/昨精确拆分，且会掩盖 B 模式的撤一笔再替换分支。
    /// </summary>
    public int AvailablePosition => Math.Max(0, TodayPosition + YesterdayPosition - FrozenPosition);
}

/// <summary>根据可平仓位得出的实际开平标志和已限制后的数量。</summary>
public sealed record CloseOrderResolution(OffsetFlag OffsetFlag, int Volume);

/// <summary>
/// 价格梯点击的开平决策。旧程序默认开仓；仅在 OnlyOpen 未启用且存在反向今/昨仓位时，
/// 才改写为平今/平昨并限制数量。总冻结数不在此处扣减，避免错过后续 B 模式容量替换。
/// </summary>
public static class CloseOrderResolver
{
    public static CloseOrderResolution Resolve(bool onlyOpen, int requestedVolume, OppositePosition oppositePosition)
    {
        if (requestedVolume <= 0)
            throw new ArgumentOutOfRangeException(nameof(requestedVolume), requestedVolume, "下单数量必须大于零");

        if (onlyOpen || oppositePosition.LegacyCloseablePosition == 0)
            return new CloseOrderResolution(OffsetFlag.Open, requestedVolume);

        var closeTodayVolume = Math.Min(requestedVolume, Math.Max(0, oppositePosition.TodayPosition));
        if (closeTodayVolume > 0)
            return new CloseOrderResolution(OffsetFlag.CloseToday, closeTodayVolume);

        var closeYesterdayVolume = Math.Min(requestedVolume, Math.Max(0, oppositePosition.YesterdayPosition));
        return new CloseOrderResolution(OffsetFlag.CloseYesterday, closeYesterdayVolume);
    }
}
