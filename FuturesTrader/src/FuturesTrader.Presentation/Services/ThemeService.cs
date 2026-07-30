using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FuturesTrader.Presentation.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// 主题服务实现:包装 WPF UI <see cref="ApplicationThemeManager"/>,
/// 持久化到 <c>user-settings.json</c>,切换时遍历所有 <see cref="FluentWindow"/> 同步 Mica 背景。
/// <para>
/// 持久化路径:<c>{AppContext.BaseDirectory}/user-settings.json</c>,与 exe 同目录,
/// 避免依赖 cwd(资源管理器双击启动时 cwd ≠ exe 目录)。
/// </para>
/// <para><b>Mica 背景刷新策略</b>(踩坑总结):</para>
/// <list type="number">
///   <item>DWM 只在 <c>DWMWA_SYSTEMBACKDROP_TYPE</c> 状态**变化**时重新合成背景
///         (Mica→None→Mica 才会触发,Mica→Mica 不触发)。</item>
///   <item><c>SetWindowPos(SWP_FRAMECHANGED)</c> 只发送 <c>WM_NCCALCSIZE</c> 重绘**非客户区**,
///         不会让 DWM 重新合成覆盖在**客户区**上的 Mica 颜色。</item>
///   <item>必须用 <c>RedrawWindow(RDW_INVALIDATE | RDW_UPDATENOW | RDW_FRAME | RDW_ERASE | RDW_ALLCHILDREN)</c>
///         强制重绘整个窗口(客户区+非客户区+子窗口),DWM 才会重新合成客户区 Mica 颜色。</item>
/// </list>
/// </summary>
public sealed class ThemeService : IThemeService
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "user-settings.json");

    private ApplicationTheme _current = ApplicationTheme.Dark;

    /// <inheritdoc />
    public ApplicationTheme Current => _current;

    /// <summary>从持久化文件加载主题(启动前调用)。</summary>
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

        // 1. 全局应用 WPF UI 主题(替换 Application.Resources 主题画刷字典)
        //    内部:更新 _cachedApplicationTheme + WindowBackgroundManager.UpdateBackground(MainWindow, ...)
        ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: true);

        // 2. 覆盖 SystemColors 画刷键:标准 WPF 控件(DataGrid/ListBox/ListView)的默认控件模板
        //    使用 SystemColors(WindowBrush/ControlTextBrush 等),不跟随 WPF UI 主题切换 → 反色。
        //    从 WPF UI 主题画刷提取颜色覆盖系统画刷键,让标准控件跟随主题。
        ApplySystemColors();

        // 3. 遍历已打开的 FluentWindow 强制刷新 Mica 背景 + 暗模式
        if (System.Windows.Application.Current is not null)
        {
            foreach (var window in System.Windows.Application.Current.Windows.OfType<FluentWindow>())
            {
                // 关键:ApplicationThemeManager.Apply(window) 只复制资源到窗口的 MergedDictionaries,
                // 不会触发 Mica/DWM 重绘。这里直接走我们自己的 RefreshWindowBackdrop。
                RefreshWindowBackdrop(window);
            }
        }
        Persist(theme);
    }

    /// <summary>
    /// 设置窗口 HWND 的 Mica 暗模式属性。在窗口 Show 之后调用(需要 HWND 已创建)。
    /// <para>
    /// 仅支持 <see cref="FluentWindow"/>(<see cref="Window"/> 无 Mica,不会更新):
    /// 普通 Window 用 <c>ApplicationBackgroundBrush</c> 直接跟随主题,无需此方法。
    /// </para>
    /// </summary>
    public void ApplyWindowDarkMode(FluentWindow window)
    {
        RefreshWindowBackdrop(window);
    }

    /// <summary>
    /// 强制刷新窗口的 Mica backdrop + 暗模式。
    /// <para><b>关键步骤(踩坑):</b></para>
    /// <list type="number">
    ///   <item>先把 <c>DWMSBT</c> 切到 <c>None</c> 再切回 <c>MainWindow</c>——
    ///         DWM 只在状态变化时重新合成背景,Mica→Mica 不会刷新。</item>
    ///   <item>用 <see cref="WindowBackdrop.ApplyBackdrop(IntPtr, WindowBackdropType)"/>
    ///         的 IntPtr 重载(不会触发 <c>RestoreContentBackground</c> 副作用,
    ///         不会把 window.Background 改成实色)。</item>
    ///   <item>用 <see cref="RedrawWindow"/> 强制整个窗口立即重绘——
    ///         <c>SetWindowPos(SWP_FRAMECHANGED)</c> 只重绘非客户区,客户区上的 Mica 不会刷新。</item>
    /// </list>
    /// </summary>
    private void RefreshWindowBackdrop(FluentWindow window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        // 1. 先切到 None (DWM 状态变化,触发后续合成)
        //    用 IntPtr 重载而非 Window 重载,避免 WindowBackdrop.RemoveBackdrop(window)
        //    内部 RestoreContentBackground 把 window.Background 改成实色 ApplicationBackgroundBrush,
        //    导致 Mica 透不出来。
        _ = WindowBackdrop.ApplyBackdrop(hwnd, WindowBackdropType.None);

        // 2. 切回 Mica。
        //    内部会先按 GetAppTheme() 调 ApplyWindowDarkMode/RemoveWindowDarkMode,
        //    再设 DWMSBT_MAINWINDOW。两个 DWM 属性变化叠加,客户区 Mica 重新合成。
        _ = WindowBackdrop.ApplyBackdrop(hwnd, WindowBackdropType.Mica);

        // 3. 确保窗口 Background 透明,让 Mica 透过客户区显示。
        //    WPF UI 内部某些路径(如 WindowBackgroundManager.UpdateBackground)会把 window.Background
        //    设为实色,这里强制还原为透明。
        window.Background = Brushes.Transparent;

        // 4. 强制整个窗口立即重绘(客户区 + 非客户区 + 子窗口)。
        //    SWP_FRAMECHANGED 只发 WM_NCCALCSIZE,重绘非客户区;客户区上的 Mica 是 DWM 合成层,
        //    必须显式 RDW_INVALIDATE | RDW_UPDATENOW 触发客户区重绘,DWM 才会重新合成。
        //    RDW_FRAME 也包含非客户区,RDW_ERASE 让背景先擦除,RDW_ALLCHILDREN 包含子窗口。
        RedrawWindow(hwnd, IntPtr.Zero, IntPtr.Zero,
            RDW_INVALIDATE | RDW_UPDATENOW | RDW_ERASE | RDW_FRAME | RDW_ALLCHILDREN);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    // RedrawWindow flags
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ERASE = 0x0004;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_FRAME = 0x0400;

    /// <summary>
    /// 覆盖 <see cref="SystemColors"/> 画刷键,让标准 WPF 控件跟随 WPF UI 主题。
    /// <para>
    /// WPF 默认控件模板用 <c>{DynamicResource {x:Static SystemColors.WindowBrushKey}}</c> 引用系统画刷。
    /// 在 <see cref="Application.Resources"/> 中覆盖该键后,DynamicResource 自动更新,控件重新渲染。
    /// </para>
    /// <para>
    /// 必须在 <see cref="ApplicationThemeManager.Apply"/> 之后调用:前者更新 WPF UI 主题画刷,
    /// 本方法从更新后的画刷提取颜色同步到 SystemColors。
    /// </para>
    /// </summary>
    private static void ApplySystemColors()
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        // 从 WPF UI 主题画刷提取颜色(ApplicationThemeManager.Apply 已更新这些画刷)
        var bgColor = (app.Resources["ApplicationBackgroundBrush"] as SolidColorBrush)?.Color;
        var fgColor = (app.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush)?.Color;
        var ctrlFillColor = (app.Resources["ControlFillColorDefaultBrush"] as SolidColorBrush)?.Color;

        if (bgColor.HasValue)
        {
            app.Resources[SystemColors.WindowBrushKey] = new SolidColorBrush(bgColor.Value);
            app.Resources[SystemColors.ControlBrushKey] = new SolidColorBrush(ctrlFillColor ?? bgColor.Value);
        }
        if (fgColor.HasValue)
        {
            app.Resources[SystemColors.WindowTextBrushKey] = new SolidColorBrush(fgColor.Value);
            app.Resources[SystemColors.ControlTextBrushKey] = new SolidColorBrush(fgColor.Value);
        }
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
