namespace FuturesTrader.Application;

/// <summary>
/// 会话状态机（discriminated union 风格）：让"非法状态无法表达"。
/// <para>
/// 合法转换路径：
/// <list type="bullet">
///   <item><see cref="Idle"/> → <see cref="LoggingIn"/> → <see cref="LoggedIn"/>（成功）</item>
///   <item><see cref="Idle"/> → <see cref="LoggingIn"/> → <see cref="Failed"/>（失败，可重试回 Idle）</item>
///   <item><see cref="LoggedIn"/> → <see cref="LoggingOut"/> → <see cref="Idle"/></item>
/// </list>
/// </para>
/// </summary>
public abstract record SessionState
{
    /// <summary>未登录（初始状态 / 登出后）。</summary>
    public sealed record Idle : SessionState;

    /// <summary>正在登录（连接行情 + 交易 + 结算确认中）。</summary>
    public sealed record LoggingIn : SessionState;

    /// <summary>已登录（行情 + 交易均已连接，可开窗交易）。</summary>
    public sealed record LoggedIn : SessionState;

    /// <summary>登录失败（含错误信息，可回 Idle 重试）。</summary>
    public sealed record Failed(string Error) : SessionState;

    /// <summary>正在登出（断开行情 + 交易连接中）。</summary>
    public sealed record LoggingOut : SessionState;
}
