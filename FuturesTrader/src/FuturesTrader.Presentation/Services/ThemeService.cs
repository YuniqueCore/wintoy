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

        // 3. 覆盖 PriceList 业务色画刷:Dark/Light 主题下红/蓝/中心高亮/控件底色的对比度不同。
        //    在 App.xaml 中定义的 PriceList* 画刷是 Dark 默认色;切到 Light 时整体替换为浅色变体,
        //    让合约窗口右侧价格梯随主题切换(用户反馈"切主题后只浮动栏切了,合约窗口不变")。
        ApplyPriceListPalette(theme);

        // 4. 遍历已打开的 FluentWindow 强制刷新 Mica 背景 + 暗模式
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

    /// <summary>
    /// 主题感知的 PriceList 调色板：替换 App.Resources 中 PriceList* 画刷的颜色，
    /// 让合约窗口右侧价格梯（背景/价格/量/中心高亮/挂单数）随主题切换。
    /// <para>
    /// Dark 用饱和红蓝（适合深底高对比），Light 用低饱和粉蓝（适合浅底柔和对比），
    /// 中心行始终用琥珀色（Yellow/Orange）保持视觉锚点。
    /// </para>
    /// <para>
    /// Hover 变体：仅用于背景；保持与默认行色 8-12% 亮度差，提供可视反馈但不抢戏。
    /// 前景色不依赖 hover 画刷，因此不会影响数字/价格可读性。
    /// </para>
    /// <para>
    /// 必须在 <see cref="ApplicationThemeManager.Apply"/> 之后调用：DynamicResource 引用这些键的控件
    /// 会在画刷实例被替换时自动重新渲染，无需手动触发 InvalidateVisual。
    /// </para>
    /// </summary>
    private static void ApplyPriceListPalette(ApplicationTheme theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        if (theme == ApplicationTheme.Light)
        {
            // Light 主题：用低饱和色，背景淡红/淡蓝/浅琥珀，文字深色
            ReplaceBrush(app, "PriceListAskRowBackgroundBrush", Color.FromRgb(0xFB, 0xE4, 0xE4));       // 淡红底
            ReplaceBrush(app, "PriceListBidRowBackgroundBrush", Color.FromRgb(0xE3, 0xEE, 0xFB));       // 淡蓝底
            ReplaceBrush(app, "PriceListAskPriceForegroundBrush", Color.FromRgb(0xC4, 0x2B, 0x1F));     // 深红文字
            ReplaceBrush(app, "PriceListBidPriceForegroundBrush", Color.FromRgb(0x1F, 0x4E, 0xA8));     // 深蓝文字
            ReplaceBrush(app, "PriceListAskVolumeForegroundBrush", Color.FromRgb(0x6B, 0x1F, 0x1F));    // 暗红
            ReplaceBrush(app, "PriceListBidVolumeForegroundBrush", Color.FromRgb(0x1F, 0x3A, 0x6B));    // 暗蓝
            ReplaceBrush(app, "PriceListCenterRowBackgroundBrush", Color.FromRgb(0xFF, 0xD7, 0x40));    // 浅琥珀
            ReplaceBrush(app, "PriceListControlBackgroundBrush", Color.FromRgb(0xF6, 0xF6, 0xF6));      // 浅灰底
            ReplaceBrush(app, "PriceListPendingOrderForegroundBrush", Color.FromRgb(0xC4, 0x6A, 0x00));  // 琥珀
            // Hover 变体：Light 主题下比默认稍深（约 8%），给出"按下去"的视觉提示但不破坏柔和配色
            ReplaceBrush(app, "PriceListAskRowHoverBackgroundBrush", Color.FromRgb(0xF1, 0xCE, 0xCE));
            ReplaceBrush(app, "PriceListBidRowHoverBackgroundBrush", Color.FromRgb(0xCE, 0xDE, 0xF1));
            ReplaceBrush(app, "PriceListCenterRowHoverBackgroundBrush", Color.FromRgb(0xFF, 0xC1, 0x07));
            ReplaceBrush(app, "CardHoverBackgroundBrush", Color.FromRgb(0xEC, 0xEC, 0xEC));
        }
        else
        {
            // Dark 主题：饱和红蓝（App.xaml 默认值）
            ReplaceBrush(app, "PriceListAskRowBackgroundBrush", Color.FromRgb(0x3D, 0x00, 0x00));
            ReplaceBrush(app, "PriceListBidRowBackgroundBrush", Color.FromRgb(0x00, 0x1F, 0x3D));
            ReplaceBrush(app, "PriceListAskPriceForegroundBrush", Color.FromRgb(0xFF, 0x66, 0x66));
            ReplaceBrush(app, "PriceListBidPriceForegroundBrush", Color.FromRgb(0x66, 0xAA, 0xFF));
            ReplaceBrush(app, "PriceListAskVolumeForegroundBrush", Color.FromRgb(0xFF, 0xAA, 0xAA));
            ReplaceBrush(app, "PriceListBidVolumeForegroundBrush", Color.FromRgb(0xAA, 0xAA, 0xFF));
            ReplaceBrush(app, "PriceListCenterRowBackgroundBrush", Color.FromRgb(0xFF, 0xC1, 0x07));
            ReplaceBrush(app, "PriceListControlBackgroundBrush", Color.FromRgb(0x1E, 0x1E, 0x1E));
            ReplaceBrush(app, "PriceListPendingOrderForegroundBrush", Color.FromRgb(0xFF, 0xD7, 0x40));
            // Hover 变体：Dark 主题下比默认稍亮（约 12%），与 WPF UI ControlFillColorSecondary 一致
            ReplaceBrush(app, "PriceListAskRowHoverBackgroundBrush", Color.FromRgb(0x5D, 0x10, 0x10));
            ReplaceBrush(app, "PriceListBidRowHoverBackgroundBrush", Color.FromRgb(0x10, 0x29, 0x4D));
            ReplaceBrush(app, "PriceListCenterRowHoverBackgroundBrush", Color.FromRgb(0xFF, 0xD7, 0x40));
            ReplaceBrush(app, "CardHoverBackgroundBrush", Color.FromRgb(0x38, 0x38, 0x38));
        }
    }

    /// <summary>替换 App.Resources 中已存在的 SolidColorBrush（保持引用身份以便 DynamicResource 触发刷新）。</summary>
    private static void ReplaceBrush(System.Windows.Application app, string key, Color color)
    {
        if (app.Resources[key] is SolidColorBrush brush)
        {
            // 不替换 brush 实例本身，只改 Color → DynamicResource 引用者会收到 Brush.Changed 通知并重绘
            brush.Color = color;
        }
        else
        {
            // 第一次进入时（如 App 启动时尚未注册），写入新实例
            app.Resources[key] = new SolidColorBrush(color);
        }
    }
}
