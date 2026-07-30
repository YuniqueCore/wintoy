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
/// <see cref="Value"/> 用作 SegmentedControl 的 <c>SelectedValue</c> 匹配（引用相等）。
/// <see cref="Label"/> 作为按钮 Content 显示。
/// <see cref="Description"/> 可选，用作 ToolTip。
/// </para>
/// </summary>
/// <remarks>
/// 故意用 <c>sealed record</c> 而非 class：record 自动实现值相等与不可变，
/// 但 SegmentedControl 内部对每项的引用仍是 OptionItem 实例，引用相等比较仍然有效。
/// </remarks>
public sealed record OptionItem(object Value, string Label, string? Description = null);
