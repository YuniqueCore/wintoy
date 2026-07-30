using System.Windows;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 浮动工具栏窗口（桌面底部长条）：WindowStyle=None + Topmost 绑定 + 底部 WorkArea 定位。
/// <para>
/// 定位策略：启动时贴屏幕工作区底部居中，高度约 96px（对齐 0527.exe 底部长条）。
/// 高度由 <see cref="Window.Height"/> 固定，不随内容变化。
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
        Height = 96;
        MinWidth = 900;
        if (Width > workArea.Width) Width = workArea.Width;
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Bottom - Height - 4;
    }
}
