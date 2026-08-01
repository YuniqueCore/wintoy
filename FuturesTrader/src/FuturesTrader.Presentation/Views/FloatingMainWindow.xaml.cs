using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
        SearchPopup.CustomPopupPlacementCallback = PlaceSearchPopup;
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
        // 仅在首次定位时设置高度（用户 resize 后不再覆盖）。
        // 默认 145 = 拖动手柄 22 + 三行内容约 120 + 边距。
        if (Height <= 100 || Height > 260)
            Height = 145;
        MinWidth = 600;
        if (Width > workArea.Width) Width = workArea.Width;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 4;
    }

    /// <summary>
    /// 将合约候选弹层限制在浮动栏所在显示器的工作区内；底部空间不足时自动向上展开。
    /// </summary>
    private CustomPopupPlacement[] PlaceSearchPopup(Size popupSize, Size targetSize, Point offset)
    {
        var presentationSource = PresentationSource.FromVisual(SearchBox);
        var fromDevice = presentationSource?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var targetTopLeft = fromDevice.Transform(SearchBox.PointToScreen(new Point(0, 0)));
        var targetBounds = new Rect(targetTopLeft, targetSize);
        var placement = SearchPopupPlacement.Calculate(popupSize, targetBounds, GetMonitorWorkArea(fromDevice));
        return [new CustomPopupPlacement(placement, PopupPrimaryAxis.Horizontal)];
    }

    private Rect GetMonitorWorkArea(Matrix fromDevice)
    {
        var monitor = MonitorFromWindow(new WindowInteropHelper(this).Handle, MonitorDefaultToNearest);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == 0 || !GetMonitorInfo(monitor, ref monitorInfo))
            return SystemParameters.WorkArea;

        var topLeft = fromDevice.Transform(new Point(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top));
        var bottomRight = fromDevice.Transform(new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    /// <summary>
    /// 拖动浮动栏：在专用拖动手柄（Row 0 顶部区域）的空白处按下鼠标左键时调用 <see cref="Window.DragMove"/>。
    /// <para>
    /// WPF 的路由事件机制保证 Button/ToggleButton 等交互控件会标记 e.Handled=true，
    /// 不会冒泡到拖动手柄 Border，因此下方按钮点击不会被误判为拖动。
    /// </para>
    /// <para>
    /// 拖动区域被刻意收窄到顶部专用 22px 横条（含可见 grip），避免在下方密集的按钮区误触
    /// 拖动——例如在按钮间隙空白处按下想点空白却触发了整窗拖动。
    /// </para>
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

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

/// <summary>合约候选弹层相对搜索框的纯定位算法。</summary>
internal static class SearchPopupPlacement
{
    internal static Point Calculate(Size popupSize, Rect targetBounds, Rect workArea, double gap = 4)
    {
        var availableBelow = workArea.Bottom - targetBounds.Bottom;
        var availableAbove = targetBounds.Top - workArea.Top;
        var openBelow = availableBelow >= popupSize.Height + gap || availableBelow >= availableAbove;

        // 搜索框位于浮动栏右侧，默认让宽弹层的右边缘与输入框右边缘对齐。
        var preferredX = targetBounds.Width - popupSize.Width;
        var preferredY = openBelow
            ? targetBounds.Height + gap
            : -popupSize.Height - gap;

        var minX = workArea.Left - targetBounds.Left;
        var maxX = workArea.Right - targetBounds.Left - popupSize.Width;
        var minY = workArea.Top - targetBounds.Top;
        var maxY = workArea.Bottom - targetBounds.Top - popupSize.Height;

        return new Point(
            ClampToWorkArea(preferredX, minX, maxX),
            ClampToWorkArea(preferredY, minY, maxY));
    }

    private static double ClampToWorkArea(double value, double minimum, double maximum) =>
        minimum <= maximum ? Math.Clamp(value, minimum, maximum) : minimum;
}
