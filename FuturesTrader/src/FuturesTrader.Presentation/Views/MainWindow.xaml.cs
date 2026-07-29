using FuturesTrader.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 主窗口 code-behind：构造函数注入 ViewModel 并设置 DataContext。
/// 段落切换由 ListBox.SelectedIndex 双向绑到 MainViewModel.CurrentSectionIndex，
/// 触发 OnCurrentSectionIndexChanged 同步 CurrentSegment，ContentControl 按运行时类型选 DataTemplate。
/// 不含业务逻辑（严格遵循 UI 表现层职责）。
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
