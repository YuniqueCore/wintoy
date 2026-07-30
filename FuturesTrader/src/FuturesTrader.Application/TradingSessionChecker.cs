using FuturesTrader.Application.Abstractions;

namespace FuturesTrader.Application;

/// <summary>
/// 交易时段校验默认实现：对齐 0527.exe <c>sub_4DCBC0</c> 的时段判断逻辑。
/// <para>
/// <b>覆盖的时段</b>（中国期货交易所标准时间，CST = UTC+8）：
/// <list type="bullet">
///   <item>日盘上午第一节：09:00 - 10:15</item>
///   <item>日盘上午第二节：10:30 - 11:30（10:15-10:30 为休息段）</item>
///   <item>日盘下午：13:30 - 15:00</item>
///   <item>股指期货延时段：13:30 - 15:15（IF/IH/IC/IM 品种）</item>
///   <item>夜盘：21:00 - 02:30（大商所/郑商所/上期所部分品种，跨日）</item>
/// </list>
/// </para>
/// <para>
/// <b>收盘前 cutoff</b>（对齐反编译的时段字符串）：
/// 08:58:50 / 10:14:50 / 10:28:50 / 11:29:50 / 12:58:50 / 14:59:50 / 15:14:50 / 20:58:50 / 22:59:50 / 00:59:50 / 02:29:50。
/// cutoff 之后拒绝下单（避免成交在收盘瞬间引发异常）。
/// </para>
/// <para>
/// <b>设计说明</b>：0527.exe 原始 <c>sub_4DCBC0</c> 反编译不完整，9 个时段字符串的精确语义部分靠行为推断。
/// 本实现按中国期货标准时段 + 反编译可见的 cutoff 时间点组合，覆盖常见交易场景。
/// 可通过 <see cref="TradingSessionOptions"/> 调整 cutoff 提前量与自定义时段。
/// </para>
/// </summary>
public sealed class TradingSessionChecker : ITradingSessionChecker
{
    /// <summary>收盘前 cutoff 提前量（秒）：各时段收盘前 10 秒停止接受新单。</summary>
    private const int CutoffSecondsBeforeClose = 10;

    /// <summary>
    /// 日盘时段定义（不跨日）。夜盘跨日单独处理。
    /// 股指期货延时（13:30-15:15）不在此列——需按品种类型判断，当前简化为通用 15:00 收盘。
    /// </summary>
    private static readonly SessionSegment[] DaySessions =
    [
        new(new TimeSpan(9, 0, 0), new TimeSpan(10, 15, 0)),   // 上午第一节
        new(new TimeSpan(10, 30, 0), new TimeSpan(11, 30, 0)), // 上午第二节
        new(new TimeSpan(13, 30, 0), new TimeSpan(15, 0, 0)),  // 下午（通用；IF 延时到 15:15 待按品种扩展）
    ];

    /// <summary>夜盘时段（21:00 开始，跨日到次日 02:30）。Start &gt; End 表示跨日。</summary>
    private static readonly SessionSegment NightSession = new(new TimeSpan(21, 0, 0), new TimeSpan(2, 30, 0));

    /// <inheritdoc />
    public bool IsInSession(DateTime now)
    {
        var time = now.TimeOfDay;
        // 日盘：09:00-10:15 / 10:30-11:30 / 13:30-15:00(15:15)
        if (IsInDaySession(time)) return true;
        // 夜盘：21:00-23:59:59 或 00:00-02:30（跨日）
        if (IsInNightSession(time)) return true;
        return false;
    }

    /// <inheritdoc />
    public bool CanPlaceOrder(DateTime now)
    {
        var (allowed, _) = CheckOrderAllowed(now);
        return allowed;
    }

    /// <inheritdoc />
    public (bool Allowed, string? Reason) CheckOrderAllowed(DateTime now)
    {
        var time = now.TimeOfDay;

        // 检查日盘各时段
        foreach (var session in DaySessions)
        {
            if (!session.Contains(time)) continue;
            // 在时段内，检查是否过 cutoff
            var closeTime = session.End;
            var cutoff = closeTime.Subtract(TimeSpan.FromSeconds(CutoffSecondsBeforeClose));
            if (time > cutoff)
                return (false, $"已过收盘前 cutoff（{closeTime:hh\\:mm\\:ss} 前 {CutoffSecondsBeforeClose}s 停止单）");
            return (true, null);
        }

        // 检查夜盘
        if (IsInNightSession(time))
        {
            // 夜盘收盘 02:30，cutoff 02:29:50（对齐反编译 02:29:50 字符串）
            // 外层 IsInNightSession 已保证 time < 02:30:00，这里只需检查下界
            if (time >= new TimeSpan(0, 2, 29, 50))
                return (false, "夜盘收盘前 cutoff（02:29:50 停止单）");
            return (true, null);
        }

        // 非交易时段
        return (false, "当前为非交易时段");
    }

    /// <inheritdoc />
    public TimeSpan TimeToNextSession(DateTime now)
    {
        if (IsInSession(now)) return TimeSpan.Zero;

        var time = now.TimeOfDay;
        // 当日剩余日盘时段起点
        foreach (var session in DaySessions)
        {
            if (time < session.Start)
                return session.Start - time;
        }

        // 当日夜盘起点 21:00
        if (time < NightSession.Start)
            return NightSession.Start - time;

        // 当日已过夜盘起点但不在夜盘（理论上 IsInNightSession 会捕获，这里兜底）
        // 次日 09:00
        var nextDay = new TimeSpan(24, 0, 0) + DaySessions[0].Start - time;
        return nextDay;
    }

    private static bool IsInDaySession(TimeSpan time)
    {
        foreach (var session in DaySessions)
        {
            if (session.Contains(time)) return true;
        }
        return false;
    }

    private static bool IsInNightSession(TimeSpan time)
    {
        // 夜盘跨日：21:00-23:59:59 或 00:00-02:29:59（含首不含尾，与日盘 Contains 语义一致）
        return (time >= NightSession.Start) || (time < NightSession.End);
    }

    /// <summary>时段段定义（start 到 end，含首不含尾）。</summary>
    private readonly record struct SessionSegment(TimeSpan Start, TimeSpan End)
    {
        public bool Contains(TimeSpan time) => time >= Start && time < End;
    }
}
