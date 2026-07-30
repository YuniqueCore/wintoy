namespace FuturesTrader.Application.Options;

/// <summary>
/// UI 相关配置：映射 appsettings.json 的 "Ui" 段。
/// 控制浮动工具栏外观与行为（主题、置顶、高度、紧凑间距）。
/// </summary>
public sealed class UiOptions
{
    /// <summary>主题：Light / Dark（启动时由 ThemeService 读取并应用）。</summary>
    public string Theme { get; init; } = "Dark";

    /// <summary>浮动工具栏是否默认置顶（Topmost）；用户可在工具栏切换。</summary>
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>浮动工具栏高度（像素，对齐 0527.exe 底部长条约 90-100px）。</summary>
    public int FloatingHeight { get; init; } = 96;

    /// <summary>多窗口水平排列紧凑间距（像素，对齐 0527.exe「窗口排列紧凑度」配置）。</summary>
    public int CompactSpacing { get; init; } = 7;
}
