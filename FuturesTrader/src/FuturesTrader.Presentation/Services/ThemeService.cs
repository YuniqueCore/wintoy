using System.IO;
using System.Text.Json;
using System.Windows;
using FuturesTrader.Presentation.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// 主题服务实现：包装 WPF UI <see cref="ApplicationThemeManager"/>，
/// 持久化到 <c>user-settings.json</c>，切换时遍历所有 <see cref="FluentWindow"/> 同步。
/// <para>
/// 持久化路径：<c>{AppContext.BaseDirectory}/user-settings.json</c>，与 exe 同目录，
/// 避免依赖 cwd（资源管理器双击启动时 cwd ≠ exe 目录）。
/// </para>
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "user-settings.json");

    private ApplicationTheme _current = ApplicationTheme.Dark;

    /// <inheritdoc />
    public ApplicationTheme Current => _current;

    /// <summary>从持久化文件加载主题（启动前调用）。</summary>
    public ApplicationTheme LoadPersisted()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("theme", out var themeEl))
                {
                    var name = themeEl.GetString();
                    if (Enum.TryParse<ApplicationTheme>(name, ignoreCase: true, out var parsed))
                        return parsed;
                }
            }
        }
        catch
        {
            // 持久化文件损坏时回退默认 Dark
        }
        return ApplicationTheme.Dark;
    }

    /// <inheritdoc />
    public void Apply(ApplicationTheme theme)
    {
        _current = theme;
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);
        // 遍历已打开的 FluentWindow 同步背景
        if (System.Windows.Application.Current is not null)
        {
            foreach (var window in System.Windows.Application.Current.Windows.OfType<FluentWindow>())
            {
                ApplicationThemeManager.Apply(window);
            }
        }
        Persist(theme);
    }

    /// <inheritdoc />
    public ApplicationTheme Toggle()
    {
        var next = _current == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark;
        Apply(next);
        return next;
    }

    private static void Persist(ApplicationTheme theme)
    {
        try
        {
            var json = JsonSerializer.Serialize(new { theme = theme.ToString() });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 持久化失败不阻断主题切换
        }
    }
}
