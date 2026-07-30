namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 登录页 UI 状态机（discriminated union 风格）：让"非法状态无法表达"。
/// <para>
/// 合法转换：Idle → Probing → Idle（测速完成）/ Idle → LoggingIn → LoginSucceeded 或 Failed
/// </para>
/// </summary>
public abstract record LoginState
{
    /// <summary>就绪（初始状态 / 测速或登录失败后回到此态）。</summary>
    public sealed record Idle : LoginState;

    /// <summary>正在测速（TCP 探测行情/交易地址延迟中）。</summary>
    public sealed record Probing : LoginState;

    /// <summary>正在登录（SessionService 连接行情 + 交易中）。</summary>
    public sealed record LoggingIn : LoginState;

    /// <summary>登录失败（含错误信息，可回 Idle 重试）。</summary>
    public sealed record Failed(string Error) : LoginState;
}
