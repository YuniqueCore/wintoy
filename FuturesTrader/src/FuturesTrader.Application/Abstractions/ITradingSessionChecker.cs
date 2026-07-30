using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 交易时段校验抽象：对齐 0527.exe <c>sub_4DCBC0</c>（@ 0x4DCBC0）的非交易时段拒单逻辑。
/// <para>
/// 0527.exe 在 <c>sub_4C036C</c>（核心下单函数）第 2 步调用本校验，非交易时段直接拒绝下单。
/// 原始实现使用 9 个时段字符串边界判断，覆盖日盘（09:00-11:30/13:30-15:00）、
/// 夜盘（21:00-02:30）、股指期货延时段（13:30-15:15）。
/// </para>
/// <para>
/// <b>收盘前最后下单时刻</b>（来自反编译）：08:58:50 / 10:28:50 / 12:58:50 / 14:58:50 等各时段收盘前约 10 秒 cutoff，
/// 02:29:50 为夜盘收盘前 cutoff。cutoff 之后下单会被拒绝（防止成交在收盘瞬间）。
/// </para>
/// </summary>
public interface ITradingSessionChecker
{
    /// <summary>
    /// 当前时间是否处于任意交易时段内（不含 cutoff 检查）。
    /// 用于 UI 状态显示（如"非交易时段"提示）。
    /// </summary>
    /// <param name="now">当前本地时间（已转换为交易所时区，CST）。</param>
    /// <returns>在交易时段内返回 true。</returns>
    bool IsInSession(DateTime now);

    /// <summary>
    /// 当前时间是否允许下单（在交易时段内 且 未过收盘前 cutoff）。
    /// 对齐 <c>sub_4C036C</c> 第 2 步校验。
    /// </summary>
    /// <param name="now">当前本地时间（CST）。</param>
    /// <returns>允许下单返回 true。</returns>
    bool CanPlaceOrder(DateTime now);

    /// <summary>
    /// 校验下单时段：返回是否允许及拒绝原因。
    /// 在 <see cref="ITradingService.SendOrderAsync"/> 前、本地风控前调用。
    /// </summary>
    /// <param name="now">当前本地时间（CST）。</param>
    /// <returns>允许返回 <c>(true, null)</c>，拒绝返回 <c>(false, 原因)</c>。</returns>
    (bool Allowed, string? Reason) CheckOrderAllowed(DateTime now);

    /// <summary>
    /// 距离下一个交易时段开始的剩余时间（用于 UI 倒计时）。
    /// 当前在时段内则返回 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    /// <param name="now">当前本地时间（CST）。</param>
    /// <returns>剩余等待时间；若当日无更多时段返回 <see cref="TimeSpan.MaxValue"/>。</returns>
    TimeSpan TimeToNextSession(DateTime now);
}
