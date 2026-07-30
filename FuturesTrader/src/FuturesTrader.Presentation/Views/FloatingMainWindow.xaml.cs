using System.Windows;
using System.Windows.Input;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 浮动工具栏窗口（桌面底部长条）：WindowStyle=None + 拖动 + ResizeGrip + 系统按钮。
/// <para>
/// 拖动：根 Border 的 MouseLeftButtonDown → <see cref="DragMove"/>（WPF 自动过滤 Button 等交互控件的点击，
/// 只有空白区域才触发拖动）。
/// 关闭/最小化：右下角系统按钮区提供 Dismiss/Subtract 图标按钮。
/// </para>
/// </summary>
public partial class FloatingMainWindow : Window
{
    /// <summary>浮动栏 ViewModel（Host 创建后注入）。</summary>
    public FloatingMainViewModel ViewModel { get; }

    public FloatingMainWindow(FloatingMainViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    /// <summary>加载完成后定位到底部居中 + 初始化分组数据。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionAtBottom();
        await ViewModel.InitializeAsync();
    }

    /// <summary>贴屏幕工作区底部居中。</summary>
    public void PositionAtBottom()
    {
        var workArea = SystemParameters.WorkArea;
        // 仅在首次定位时设置高度（用户 resize 后不再覆盖）
        if (Height <= 100 || Height > 240)
            Height = 120;
        MinWidth = 600;
        if (Width > workArea.Width) Width = workArea.Width;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 4;
    }

    /// <summary>
    /// 拖动浮动栏：在根 Border 的空白区域按下鼠标左键时调用 <see cref="Window.DragMove"/>。
    /// WPF 的路由事件机制保证 Button/ToggleButton 等交互控件会标记 e.Handled=true，
    /// 不会冒泡到 Border，因此按钮点击不会被误判为拖动。
    /// </summary>
    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { /* DragMove 在某些状态下会抛 InvalidOperationException，忽略 */ }
        }
    }

    /// <summary>最小化浮动栏到任务栏。</summary>
    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>
    /// 退出程序：与「登出」语义不同——登出仅断开会话返回登录页，退出则关闭整个应用。
    /// 触发 <see cref="App.OnExit"/> 自动登出会话（关闭合约窗口 + 断开 CTP）。
    /// </summary>
    private void OnClose(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
