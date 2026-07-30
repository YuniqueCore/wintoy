using System.Windows;
using FuturesTrader.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 设置窗口：从登录页或浮动栏的「设置」按钮打开。
/// 左栏 5 段导航（Window/Order/User/窗口分组/外观），右栏表单按段类型自动选 DataTemplate。
/// 主题切换（外观段）即时生效并持久化；config.ini 三段需手动加载/保存。
/// </summary>
public partial class SettingsWindow : FluentWindow
{
    /// <summary>设置 ViewModel（Host 通过 DI 注入）。</summary>
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }
}
