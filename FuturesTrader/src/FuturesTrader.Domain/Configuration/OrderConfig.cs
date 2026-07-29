namespace FuturesTrader.Domain.Configuration;

/// <summary>
/// 对应 config.ini [Order] 段：交易风控参数。
/// </summary>
public sealed record OrderConfig
{
    /// <summary>商品撤单开关（SP=商品）</summary>
    public bool Spck { get; init; }

    /// <summary>股指撤单开关（GZ=股指）</summary>
    public bool Gzck { get; init; }

    /// <summary>本地风控开关</summary>
    public bool RiskOpen { get; init; }

    /// <summary>股指最大撤单数（CTP 风控要求，防撤单过量被限制）</summary>
    public int MaxCancelGz { get; init; } = 395;

    /// <summary>商品最大撤单数</summary>
    public int MaxCancelSp { get; init; } = 10000;

    /// <summary>期权最大撤单数（QQ=期权）</summary>
    public int MaxCancelQq { get; init; } = 10000;

    /// <summary>最大报单数限制（0=不限制）</summary>
    public int MaxInputCount { get; init; }

    /// <summary>最大持仓数限制（0=不限制）</summary>
    public int MaxPositionCount { get; init; }
}
