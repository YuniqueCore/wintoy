using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 本地风控服务抽象：在报单提交 CTP 前做本地校验，避免触发交易所/期货公司风控限制。
/// 对齐 0527.exe config.ini [Order] 段的 Spck/Gzck/MaxCancel*/MaxInput*/MaxPosition* 参数。
/// <para>
/// 校验维度：
/// <list type="bullet">
///   <item><b>报单数限制</b>：<see cref="OrderConfig.MaxInputCount"/>（0=不限制），防止刷单。</item>
///   <item><b>持仓数限制</b>：<see cref="OrderConfig.MaxPositionCount"/>（0=不限制），防过度持仓。</item>
///   <item><b>撤单数限制</b>：按品种类型（股指 GZ/商品 SP/期权 QQ）分别计数，
///     <see cref="OrderConfig.Spck"/>/<see cref="OrderConfig.Gzck"/> 控制开关，
///     超过 <see cref="OrderConfig.MaxCancelGz"/>/<see cref="OrderConfig.MaxCancelSp"/>/<see cref="OrderConfig.MaxCancelQq"/> 拒绝撤单。</item>
/// </list>
/// <see cref="OrderConfig.RiskOpen"/> 为总开关，false 时所有规则放行。
/// </para>
/// </summary>
public interface ILocalRiskService
{
    /// <summary>
    /// 校验报单请求是否通过本地风控。
    /// 在 <see cref="ITradingService.SendOrderAsync"/> 调用 CTP 前执行。
    /// </summary>
    /// <param name="request">待提交的报单请求。</param>
    /// <param name="currentOrderCount">当前会话已提交报单总数。</param>
    /// <param name="currentPositionCount">当前持仓合约数。</param>
    /// <returns>校验结果：通过返回 <c>(true, null)</c>，拒绝返回 <c>(false, 拒绝原因)</c>。</returns>
    (bool Allowed, string? Reason) CheckOrder(OrderRequest request, int currentOrderCount, int currentPositionCount);

    /// <summary>
    /// 校验撤单请求是否通过本地风控（撤单计数限制）。
    /// 在 <see cref="ITradingService.CancelOrderAsync"/> 调用 CTP 前执行。
    /// </summary>
    /// <param name="instrumentId">合约代码（用于判断品种类型：股指/商品/期权）。</param>
    /// <param name="cancelCounts">各品种类型当前撤单计数（GZ/SP/QQ）。</param>
    /// <returns>校验结果：通过返回 <c>(true, null)</c>，拒绝返回 <c>(false, 拒绝原因)</c>。</returns>
    (bool Allowed, string? Reason) CheckCancel(string instrumentId, RiskCancelCounters cancelCounts);

    /// <summary>记录一次撤单（通过校验后调用，更新内部计数）。</summary>
    void RecordCancel(string instrumentId);

    /// <summary>重置计数器（新交易日/重连时调用）。</summary>
    void Reset();

    /// <summary>
    /// 当前各品种类型的撤单计数快照（股指 GZ/商品 SP/期权 QQ）。
    /// 调用方传给 <see cref="CheckCancel"/> 做校验，与 <see cref="RecordCancel"/> 共同维护服务内部计数。
    /// </summary>
    RiskCancelCounters CurrentCounters { get; }
}

/// <summary>
/// 风控撤单计数器：按品种类型（股指 GZ/商品 SP/期权 QQ）分别统计当日撤单数。
/// 对齐 CTP 风控规则：股指 500 次/日、商品 10000 次/日、期权 10000 次/日（SimNow 宽松）。
/// </summary>
public sealed record RiskCancelCounters
{
    /// <summary>股指撤单计数。</summary>
    public int GzCount { get; init; }

    /// <summary>商品撤单计数。</summary>
    public int SpCount { get; init; }

    /// <summary>期权撤单计数。</summary>
    public int QqCount { get; init; }
}
