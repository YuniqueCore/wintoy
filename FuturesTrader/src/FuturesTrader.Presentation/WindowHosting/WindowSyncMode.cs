namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// 窗口同步模式：控制分组内合约窗口是否成组联动。
/// <para>
/// <see cref="Grouped"/>（默认）：拖动/缩放任一窗口，同组其他窗口实时跟随；
/// <see cref="Independent"/>：每个窗口完全独立，互不影响。
/// </para>
/// 对齐 0527.exe「默认成组控制，有开关切独立」的交互。
/// </summary>
public enum WindowSyncMode
{
    /// <summary>成组同步：拖动/缩放联动同组窗口（默认）。</summary>
    Grouped,

    /// <summary>完全独立：窗口各自独立，不联动。</summary>
    Independent
}
