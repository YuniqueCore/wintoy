using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FuturesTrader.Presentation.Controls;

/// <summary>
/// 状态感知图标切换按钮：标准 <see cref="ToggleButton"/> 的轻量 UserControl 包装，
/// 用于浮动栏 / 系统工具栏的「开/关」型开关（置顶、同步、跟随等二元状态）。
/// <para>
/// <b>视觉语义</b>：
/// <list type="bullet">
///   <item>OFF（<see cref="IsChecked"/>=false）：透明背景 + 次要前景色</item>
///   <item>Hover：浅灰背景（ControlFillColorSecondary）</item>
///   <item>Pressed：深灰背景（ControlFillColorTertiary）</item>
///   <item>ON（<see cref="IsChecked"/>=true）：Accent 填充 + 白色前景，与 SegmentedControl 选中段一致</item>
/// </list>
/// </para>
/// <para>
/// 对齐用户「switcher 开启/关闭都需要清晰反馈」约束：复用与 <c>SegmentedControl</c>
/// 一致的 IsChecked 触发器（Accent 填充），保证多个 switcher 视觉语言统一。
/// <c>FocusVisualStyle={x:Null}</c> 移除 WPF 默认的红色焦点虚线框。
/// </para>
/// <para>
/// 用法（XAML）：
/// <code>
/// &lt;controls:StateToggleButton IsChecked="{Binding AlwaysOnTop, Mode=TwoWay}"
///                              Command="{Binding ToggleTopmostCommand}"
///                              Icon="{ui:SymbolIcon Pin24}"
///                              ToolTip="切换置顶"/&gt;
/// </code>
/// </para>
/// </summary>
public sealed partial class StateToggleButton : UserControl
{
    public StateToggleButton()
    {
        InitializeComponent();
    }

    /// <summary>开关状态（TwoWay 绑定到 ViewModel 的 bool 属性）。</summary>
    public bool? IsChecked
    {
        get => (bool?)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register(
            nameof(IsChecked),
            typeof(bool?),
            typeof(StateToggleButton),
            new FrameworkPropertyMetadata(
                false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    /// <summary>点击触发的命令（与 ToggleButton.Command 同语义：每次点击触发 Execute，IsChecked 状态由 ViewModel 管理）。</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(ICommand),
            typeof(StateToggleButton),
            new PropertyMetadata(null));

    /// <summary>命令参数。</summary>
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(
            nameof(CommandParameter),
            typeof(object),
            typeof(StateToggleButton),
            new PropertyMetadata(null));

    /// <summary>图标内容（任意 UIElement，推荐 <c>SymbolIcon</c>）。会作为内部 ContentControl 的 Content 渲染。</summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(object),
            typeof(StateToggleButton),
            new PropertyMetadata(null));

    // ToolTip 属性直接复用 FrameworkElement.ToolTip（继承自 UserControl → Control → FrameworkElement），
    // 不需要自定义 DP。XAML 中 ToolTip="{Binding ToolTip, ElementName=Root}" 直接绑定到继承属性。
}
