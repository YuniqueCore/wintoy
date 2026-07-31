namespace FuturesTrader.Domain.Trading;

/// <summary>
/// <c>RunMode</c> 选择的旧版点价下单函数族。它不是 A/B 挂单模式；
/// A/B 由每个合约窗口的 <c>RBOA</c>/<c>RBOB</c> 单选状态决定。
/// </summary>
public enum LegacyPriceLadderOrderPath
{
    /// <summary><c>RunMode=0/1</c> 及未命中替代掩码的值所使用的标准函数族。</summary>
    Standard,

    /// <summary><c>RunMode=2/3</c> 所使用的替代函数族。</summary>
    Alternate
}

/// <summary>
/// 从旧版 config.ini [User] RunMode 恢复的运行时交易分支。
/// 旧程序用 <c>(RunMode &amp; 0xFFFFFFFE) == 2</c> 选择替代函数族，
/// 因而不能把它简化成 <c>RunMode == 0</c>/<c>RunMode != 0</c>。
/// 未恢复产品名称，故保留原始整数而不虚构业务枚举名称。
/// </summary>
public sealed record LegacyTradingRuntime(int RunMode = 0)
{
    /// <summary>旧入口实际选择的下单函数族。</summary>
    public LegacyPriceLadderOrderPath PriceLadderOrderPath =>
        (RunMode & unchecked((int)0xFFFFFFFE)) == 2
            ? LegacyPriceLadderOrderPath.Alternate
            : LegacyPriceLadderOrderPath.Standard;

    /// <summary>
    /// 当前端口已逐项实现标准函数族。替代族还依赖旧窗体中未恢复的数量阈值和 EZMode 等状态，
    /// 不能静默降级为标准路径后继续报单。
    /// </summary>
    public bool SupportsPortedPriceLadderOrders => PriceLadderOrderPath == LegacyPriceLadderOrderPath.Standard;

    /// <summary>
    /// 旧 XML 写出仅在 RunMode=1/2 分支包含 CBOC；这与其在 B 平仓路径中的运行时参与范围不同。
    /// </summary>
    public bool PersistsCbOc => RunMode is 1 or 2;

    /// <summary>
    /// 旧 XML 写出在非 1/2 分支包含 CBBGDS/CBZDTlock，而 1/2 分支写出另一组扩展交易控件。
    /// </summary>
    public bool PersistsQuoteLockFields => !PersistsCbOc;

    /// <summary>用户可见的安全拒绝原因；标准族返回 <see langword="null"/>。</summary>
    public string? GetUnsupportedPriceLadderOrderReason() => SupportsPortedPriceLadderOrders
        ? null
        : $"RunMode={RunMode} 使用旧版替代点价路径；其数量阈值与 EZMode 约束尚未完整恢复，已阻止报单。";

    /// <summary>
    /// 标准和替代 B 路径均在非零 RunMode 且 CBOC 未勾选时，
    /// 先请求撤销同合约同方向的开仓挂单。
    /// </summary>
    public BModeClosePolicy ResolveBModeClosePolicy(bool cbOc) =>
        new(CancelSameDirectionOpenOrders: RunMode != 0 && !cbOc);
}

/// <summary>B 模式平仓前对同方向开仓挂单的策略。</summary>
public readonly record struct BModeClosePolicy(bool CancelSameDirectionOpenOrders);
