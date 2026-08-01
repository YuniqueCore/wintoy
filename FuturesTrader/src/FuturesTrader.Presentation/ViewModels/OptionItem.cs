namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 通用「值 + 显示文本」选项对：用于 <c>SegmentedControl.ItemsSource</c>。
/// <para>
/// 用法：
/// <code>
/// Options = new[]
/// {
///     new OptionItem(FloatingOrderMode.Open, "仓"),
///     new OptionItem(FloatingOrderMode.Close, "平"),
/// };
/// </code>
/// </para>
/// <para>
/// <see cref="Value"/> 用作 SegmentedControl 的 <c>SelectedValue</c> 匹配（值相等）。
/// <see cref="Label"/> 作为按钮 Content 显示。
/// <see cref="Description"/> 可选，用作 ToolTip。
/// </para>
/// </summary>
/// <remarks>
/// 故意用 <c>sealed record</c> 而非 class：record 自动实现值相等与不可变，
/// SegmentedControl 通过显式 <c>ValueSelector="Value"</c> 读取枚举值，避免把整个选项对象写回枚举属性。
/// </remarks>
public sealed record OptionItem(object Value, string Label, string? Description = null);
