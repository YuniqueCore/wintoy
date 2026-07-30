using FuturesTrader.Application.Abstractions;

namespace FuturesTrader.Application;

/// <summary>
/// 价差计算默认实现：对齐 0527.exe CntrbySprd 控件家族的价差计算公式。
/// <para>
/// <b>公式</b>（来自 <c>interaction-cntrbysprd.md</c>）：
/// <list type="bullet">
///   <item><c>spreadPrice = basePrice ± (factor × tickSize)</c>（sub_4C4C5C）</item>
///   <item><c>displayPrice = ladderBase + spreadPrice + spreadInstrumentPrice</c>（sub_4BC6C8）</item>
/// </list>
/// </para>
/// <para>
/// 纯逻辑、无副作用、线程安全。可独立单元测试，不依赖 UI/网络/文件系统。
/// </para>
/// </summary>
public sealed class SpreadCalculator : ISpreadCalculator
{
    /// <inheritdoc />
    public decimal CalculateSpreadPrice(decimal basePrice, int factor, decimal tickSize, SpreadDirection direction)
    {
        if (tickSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(tickSize), tickSize, "最小变动价位必须 > 0");
        if (factor < 0)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "价差系数必须 >= 0");

        var delta = factor * tickSize;
        return direction switch
        {
            SpreadDirection.Add => basePrice + delta,
            SpreadDirection.Subtract => basePrice - delta,
            _ => basePrice
        };
    }

    /// <inheritdoc />
    public decimal CalculateDisplayPrice(decimal ladderBase, decimal spreadPrice, decimal spreadInstrumentPrice)
    {
        // 对齐 sub_4BC6C8：displayPrice = ladderBase + spreadPrice + spreadInstrumentPrice
        // spreadInstrumentPrice 为价差合约（另一腿）的最新价；无价差合约时调用方传 0
        return ladderBase + spreadPrice + spreadInstrumentPrice;
    }

    /// <inheritdoc />
    public (bool Valid, string? Reason) Validate(SpreadConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // 互斥校验：普通与扩展不能同时启用
        if (config.IsNormalEnabled && config.IsExtendedEnabled)
            return (false, "普通价差与扩展价差不能同时启用（互斥）");

        // 启用普通价差时，合约 ID 必须非空
        if (config.IsNormalEnabled && string.IsNullOrWhiteSpace(config.NormalInstrumentId))
            return (false, "普通价差已启用但未填写价差合约 ID");

        // 启用扩展价差时，合约 ID 必须非空
        if (config.IsExtendedEnabled && string.IsNullOrWhiteSpace(config.ExtendedInstrumentId))
            return (false, "扩展价差已启用但未填写价差合约 ID");

        // TickSize 必须有效
        if (config.TickSize <= 0)
            return (false, "最小变动价位必须 > 0");

        // 系数可以为 0（相当于无价差偏移），但不应为负
        if (config.Factor < 0)
            return (false, "价差系数不能为负");

        return (true, null);
    }
}
