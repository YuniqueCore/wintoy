using FuturesTrader.Domain.Connections;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 会话服务：登录后会话状态的单一所有者（当前账号 + 已连接的行情/交易服务）。
/// 编排登录流程：行情连接 → 交易连接（认证→登录→结算确认）。
/// <para>
/// 登录成功后 <see cref="MarketData"/> / <see cref="Trading"/> 可用，供 FloatingMainWindow / ContractWindow 使用。
/// 登出时显式 DisposeAsync 释放 CTP 连接。
/// </para>
/// </summary>
public interface ISessionService
{
    /// <summary>当前会话状态（状态机）。</summary>
    SessionState CurrentState { get; }

    /// <summary>当前登录账号；未登录时为 null。</summary>
    AccountEntry? Account { get; }

    /// <summary>当前行情服务；未登录时为 null。</summary>
    IMarketDataService? MarketData { get; }

    /// <summary>当前交易服务；未登录时为 null。</summary>
    ITradingService? Trading { get; }

    /// <summary>会话状态变更事件（UI 订阅后切换窗口/按钮可用性）。</summary>
    event EventHandler<SessionState>? StateChanged;

    /// <summary>
    /// 登录：用选择的账号 + 行情地址 + 密码创建并连接行情/交易服务。
    /// 成功后 <see cref="CurrentState"/> = <see cref="SessionState.LoggedIn"/>。
    /// </summary>
    Task<SessionState> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 登出：断开并释放行情/交易服务，回到 <see cref="SessionState.Idle"/>。
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 登录请求值对象：用户在登录页选择的所有连接参数。
/// </summary>
public sealed record LoginRequest(
    AccountEntry Account,
    HqAddressEntry HqAddress,
    string Password,
    bool RefreshInstruments);
