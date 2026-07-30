namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 价差计算抽象：对齐 0527.exe CntrbySprd 控件家族的价差计算逻辑。
/// <para>
/// <b>原始公式</b>（来自 <c>interaction-cntrbysprd.md</c>）：
/// <list type="bullet">
///   <item><c>spreadPrice = basePrice ± (factor × tickSize)</c>（sub_4C4C5C，左键加/右键减）</item>
///   <item><c>displayPrice = ladderBase + spreadPrice + spreadInstrumentPrice</c>（sub_4BC6C8）</item>
/// </list>
/// 系数 <c>factor</c>（TXCntrbySprdFctn）为普通价差与扩展价差<b>共享</b>。
/// </para>
/// <para>
/// 普通价差（CBCntrbySprd）与扩展价差（CBCntrbySprdEX）<b>互斥</b>，同一时刻只能启用一个。
/// </para>
/// </summary>
public interface ISpreadCalculator
{
    /// <summary>
    /// 计算价差价格（基准价 ± 系数 × 最小变动价位）。
    /// 对齐 <c>sub_4C4C5C</c>：左键点击 PT 文本框为加，右键为减。
    /// </summary>
    /// <param name="basePrice">基准价（用户在 PT 文本框输入的价格）。</param>
    /// <param name="factor">价差系数（整数，正数；左键加/右键减决定符号）。</param>
    /// <param name="tickSize">最小变动价位（来自合约元数据 PriceTick）。</param>
    /// <param name="direction">价差方向：<c>Add</c>（左键，+）或 <c>Subtract</c>（右键，−）。</param>
    /// <returns>计算后的价差价格。</returns>
    decimal CalculateSpreadPrice(decimal basePrice, int factor, decimal tickSize, SpreadDirection direction);

    /// <summary>
    /// 计算叠加价差后的显示价格。
    /// 对齐 <c>sub_4BC6C8</c>：<c>displayPrice = ladderBase + spreadPrice + spreadInstrumentPrice</c>。
    /// </summary>
    /// <param name="ladderBase">价格 ladder 基础价（当前合约的某档价格）。</param>
    /// <param name="spreadPrice">价差价格（由 <see cref="CalculateSpreadPrice"/> 计算）。</param>
    /// <param name="spreadInstrumentPrice">价差合约的当前价（另一腿合约的最新价；无价差合约时传 0）。</param>
    /// <returns>叠加后的显示价格。</returns>
    decimal CalculateDisplayPrice(decimal ladderBase, decimal spreadPrice, decimal spreadInstrumentPrice);

    /// <summary>
    /// 校验价差配置是否有效（启用普通或扩展价差时，合约 ID 与系数必须有效）。
    /// </summary>
    /// <param name="config">价差配置。</param>
    /// <returns>有效返回 <c>(true, null)</c>，无效返回 <c>(false, 原因)</c>。</returns>
    (bool Valid, string? Reason) Validate(SpreadConfig config);
}

/// <summary>价差方向：左键加（Add）/ 右键减（Subtract）。</summary>
public enum SpreadDirection
{
    /// <summary>左键点击：价差价格 = 基准价 + 系数 × tick。</summary>
    Add,

    /// <summary>右键点击：价差价格 = 基准价 − 系数 × tick。</summary>
    Subtract
}

/// <summary>
/// 价差配置（对应 CntrbySprd 7 控件家族的状态）。
/// 对齐 <c>interaction-cntrbysprd.md</c> §3 的对象字段偏移。
/// </summary>
public sealed record SpreadConfig
{
    /// <summary>是否启用普通价差（CBCntrbySprd，偏移 +1156）。与 <see cref="IsExtendedEnabled"/> 互斥。</summary>
    public bool IsNormalEnabled { get; init; }

    /// <summary>是否启用扩展价差（CBCntrbySprdEX，偏移 +1296）。与 <see cref="IsNormalEnabled"/> 互斥。</summary>
    public bool IsExtendedEnabled { get; init; }

    /// <summary>普通价差合约 ID（TXCntrbySprdID，偏移 +1160）。</summary>
    public string? NormalInstrumentId { get; init; }

    /// <summary>普通价差基准价（TXCntrbySprdPT，偏移 +1164）。</summary>
    public decimal NormalBasePrice { get; init; }

    /// <summary>扩展价差合约 ID（TXCntrbySprdIDEX，偏移 +1292）。</summary>
    public string? ExtendedInstrumentId { get; init; }

    /// <summary>扩展价差基准价（TXCntrbySprdPTEX，偏移 +1288）。</summary>
    public decimal ExtendedBasePrice { get; init; }

    /// <summary>
    /// 价差系数（TXCntrbySprdFctn，偏移 +1168）。
    /// 普通与扩展<b>共享</b>同一系数（反编译证据：sub_4CEEEC 第 109365 行读取 +1168）。
    /// </summary>
    public int Factor { get; init; }

    /// <summary>最小变动价位（来自当前合约元数据，非控件状态）。</summary>
    public decimal TickSize { get; init; } = 1m;

    /// <summary>当前激活的价差类型（互斥后唯一生效的）。</summary>
    public SpreadActiveType ActiveType =>
        IsNormalEnabled ? SpreadActiveType.Normal :
        IsExtendedEnabled ? SpreadActiveType.Extended :
        SpreadActiveType.None;
}

/// <summary>当前激活的价差类型。</summary>
public enum SpreadActiveType
{
    /// <summary>未启用价差。</summary>
    None,

    /// <summary>普通价差（CBCntrbySprd）生效。</summary>
    Normal,

    /// <summary>扩展价差（CBCntrbySprdEX）生效。</summary>
    Extended
}
