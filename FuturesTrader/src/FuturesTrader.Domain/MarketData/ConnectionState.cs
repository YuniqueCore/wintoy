namespace FuturesTrader.Domain.MarketData;

/// <summary>
/// 行情/交易连接状态机（替代零散 bool）。密封子类保证非法状态无法表达。
/// 与 06-refactor-guide.md §4.4 一致。
/// </summary>
public abstract record ConnectionState
{
    private ConnectionState() { }

    /// <summary>未连接。</summary>
    public sealed record Disconnected : ConnectionState;

    /// <summary>连接中。</summary>
    public sealed record Connecting : ConnectionState;

    /// <summary>已连接。</summary>
    public sealed record Connected : ConnectionState;

    /// <summary>断线重连中：第 <c>Attempt</c> 次，<c>NextRetry</c> 后重试。</summary>
    public sealed record Reconnecting(int Attempt, TimeSpan NextRetry) : ConnectionState;

    /// <summary>连接失败：<c>Error</c> 描述原因。</summary>
    public sealed record Failed(string Error) : ConnectionState;
}
