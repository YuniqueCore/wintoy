using System.Windows;
using FuturesTrader.Presentation.ViewModels;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Views;

/// <summary>
/// 登录窗口代码后置：仅处理 PasswordBox 密码绑定（WPF Password 密码非 DP，需 code-behind 桥接）。
/// 业务逻辑全部在 <see cref="LoginViewModel"/>，遵循 MVVM。
/// </summary>
public partial class LoginWindow : FluentWindow
{
    /// <summary>登录 ViewModel（Host 订阅其 LoginSucceeded / OpenSettingsRequested 事件）。</summary>
    public LoginViewModel ViewModel { get; }

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>PasswordBox 密码变更 → 同步到 VM（WPF Password 非 DP，需手动桥接）。</summary>
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ViewModel.Password = PasswordBox.Password;
    }
}
