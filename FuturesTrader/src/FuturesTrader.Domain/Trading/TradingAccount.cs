namespace FuturesTrader.Domain.Trading;

/// <summary>
/// 资金账户值对象：CTP <c>OnRspQryTradingAccount</c> → 映射为不可变 record 推送到 <see cref="ITradingService.AccountStream"/>。
/// 每次查询推送一条记录（CTP 资金账户通常单条）。
/// <para>
/// <b>浮动栏「市/净/可/持/权/手」字段映射</b>（见 <c>floating.main.window.md</c>）：
/// <list type="bullet">
///   <item>市（市值）→ <see cref="MarketValue"/>（= Equity - Available + FrozenMargin + FrozenCash，近似）</item>
///   <item>净（净盈亏）→ <see cref="PositionProfit"/> + <see cref="CloseProfit"/></item>
///   <item>可（可用资金）→ <see cref="Available"/></item>
///   <item>持（持仓手数）→ 从 <see cref="Position"/> 流聚合（不在本记录内）</item>
///   <item>权（权益）→ <see cref="Equity"/></item>
///   <item>手（成交手数）→ 从 <see cref="Trade"/> 流聚合（不在本记录内）</item>
/// </list>
/// </para>
/// <para>
/// <b>关键字段映射</b>（CTP → Domain）：
/// <list type="bullet">
///   <item><c>AccountID</c> → <see cref="AccountId"/></item>
///   <item><c>Balance</c> → <see cref="Balance"/>（账户余额 = 动态权益）</item>
///   <item><c>Available</c> → <see cref="Available"/>（可用资金）</item>
///   <item><c>FrozenMargin</c> → <see cref="FrozenMargin"/></item>
///   <item><c>FrozenCash</c> → <see cref="FrozenCash"/></item>
///   <item><c>FrozenCommission</c> → <see cref="FrozenCommission"/></item>
///   <item><c>CurrMargin</c> → <see cref="Margin"/>（当前保证金占用）</item>
///   <item><c>Commission</c> → <see cref="Commission"/></item>
///   <item><c>PositionProfit</c> → <see cref="PositionProfit"/></item>
///   <item><c>CloseProfit</c> → <see cref="CloseProfit"/></item>
///   <item><c>WithdrawBalance</c> → <see cref="WithdrawBalance"/></item>
/// </list>
/// <see cref="Equity"/> = <see cref="Balance"/> - <see cref="WithdrawBalance"/>（投资者权益，部分期货公司 Balance 即权益）。
/// </para>
/// </summary>
public sealed record TradingAccount
{
    /// <summary>资金账号（CTP AccountID）。</summary>
    public string AccountId { get; init; } = string.Empty;

    /// <summary>账户余额（CTP Balance，动态权益 = 上日结存 + 出入金 + 平仓盈亏 + 持仓盈亏 - 手续费）。</summary>
    public decimal Balance { get; init; }

    /// <summary>可用资金（CTP Available，可开仓的剩余资金）。</summary>
    public decimal Available { get; init; }

    /// <summary>投资者权益（CTP 无直接字段，= Balance - WithdrawBalance；多数期货公司 Balance 即权益）。</summary>
    public decimal Equity { get; init; }

    /// <summary>市值（持仓按最新价计算的市值，= 持仓成本 + 持仓盈亏）。</summary>
    public decimal MarketValue { get; init; }

    /// <summary>持仓盈亏（CTP PositionProfit，浮动盈亏）。</summary>
    public decimal PositionProfit { get; init; }

    /// <summary>平仓盈亏（CTP CloseProfit，当日已平仓盈亏）。</summary>
    public decimal CloseProfit { get; init; }

    /// <summary>当前保证金占用（CTP CurrMargin）。</summary>
    public decimal Margin { get; init; }

    /// <summary>冻结保证金（CTP FrozenMargin，挂单冻结）。</summary>
    public decimal FrozenMargin { get; init; }

    /// <summary>冻结资金（CTP FrozenCash，含冻结手续费/保证金等）。</summary>
    public decimal FrozenCash { get; init; }

    /// <summary>冻结手续费（CTP FrozenCommission）。</summary>
    public decimal FrozenCommission { get; init; }

    /// <summary>手续费（CTP Commission，当日已发生）。</summary>
    public decimal Commission { get; init; }

    /// <summary>出金金额（CTP WithdrawBalance，当日累计出金）。</summary>
    public decimal WithdrawBalance { get; init; }
}
