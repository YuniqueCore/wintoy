using Wpf.Ui.Appearance;

namespace FuturesTrader.Presentation.Abstractions;

/// <summary>
/// 主题服务抽象：统一应用主题（Light/Dark）并持久化用户选择。
/// 启动时由 Host 调用 <see cref="Apply"/> 应用持久化主题；
/// 切换时遍历所有 <see cref="FluentWindow"/> 同步背景（Mica/MicaAlt）。
/// </summary>
public interface IThemeService
{
    /// <summary>当前主题。</summary>
    ApplicationTheme Current { get; }

    /// <summary>应用指定主题（同步所有已开窗口）。</summary>
    void Apply(ApplicationTheme theme);

    /// <summary>切换 Light ↔ Dark 并持久化。</summary>
    ApplicationTheme Toggle();
}
