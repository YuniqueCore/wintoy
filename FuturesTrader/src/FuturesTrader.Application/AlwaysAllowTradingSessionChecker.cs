using FuturesTrader.Application.Abstractions;

namespace FuturesTrader.Application;

/// <summary>
/// Mock/开发交易服务专用的时段策略：允许在任意时间验证完整报单与撤单 UI 链路。
/// 生产 CTP 组合根不得注册此实现。
/// </summary>
public sealed class AlwaysAllowTradingSessionChecker : ITradingSessionChecker
{
    /// <inheritdoc />
    public bool IsInSession(DateTime now) => true;

    /// <inheritdoc />
    public bool CanPlaceOrder(DateTime now) => true;

    /// <inheritdoc />
    public (bool Allowed, string? Reason) CheckOrderAllowed(DateTime now) => (true, null);

    /// <inheritdoc />
    public TimeSpan TimeToNextSession(DateTime now) => TimeSpan.Zero;
}
