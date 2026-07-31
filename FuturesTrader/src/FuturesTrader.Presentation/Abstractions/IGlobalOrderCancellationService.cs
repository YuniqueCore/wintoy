namespace FuturesTrader.Presentation.Abstractions;

/// <summary>系统级撤单的请求范围。</summary>
public enum GlobalOrderCancellationMode
{
    /// <summary>仅请求当前可见的交易窗撤单，作为旧程序选择性全撤的保守映射。</summary>
    SelectiveVisibleWindows,

    /// <summary>请求所有已注册交易窗撤单。仅供明确的强制入口使用。</summary>
    ForceAllWindows
}

/// <summary>系统级撤单请求的本地派发结果，不代表交易所已确认撤单。</summary>
public sealed record GlobalOrderCancellationResult(int TargetWindowCount, int FailedWindowCount);

/// <summary>
/// 汇集多个合约窗口的撤单入口。注册项只暴露取消动作和选择性资格，
/// 使全局快捷键不需要直接持有具体 Window 或 ViewModel。
/// </summary>
public interface IGlobalOrderCancellationService
{
    IDisposable Register(Func<Task> cancelAll, Func<bool> isSelectivelyEligible);

    Task<GlobalOrderCancellationResult> CancelAsync(GlobalOrderCancellationMode mode);
}
