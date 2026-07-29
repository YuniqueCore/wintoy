using System.Windows;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 主窗口 code-behind：仅做构造函数注入 ViewModel 并设置 DataContext。
/// 不含业务逻辑（严格遵循 UI 表现层职责）。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
