namespace FuturesTrader.Presentation.ViewModels;

/// <summary>显示范围模式：单（仅当前组）/ 多（多个组）/ 全部（所有组）。对齐浮动栏「单/多/全部」单选。</summary>
public enum FloatingDisplayMode
{
    /// <summary>单：仅显示当前选中分组。</summary>
    Single,

    /// <summary>多：显示多个分组。</summary>
    Multi,

    /// <summary>全部：显示全部分组。</summary>
    All
}

/// <summary>开平仓模式：仓（开仓）/ 平（平仓）。对齐浮动栏「仓/平」单选，与点价窗口 OnlyOpen 联动。</summary>
public enum FloatingOrderMode
{
    /// <summary>仓：开仓模式（对应 OnlyOpen=true）。</summary>
    Open,

    /// <summary>平：平仓模式（对应 OnlyOpen=false，P 标识）。</summary>
    Close
}

/// <summary>挂单模式 A/B：对应点价窗口 ChgOrder(A)/(B)。A=单方向单点，B=单方向多点。</summary>
public enum FloatingAbMode
{
    /// <summary>A：单方向单一点位挂单。</summary>
    A,

    /// <summary>B：单方向多个点位挂单。</summary>
    B
}
