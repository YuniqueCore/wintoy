using System.IO;
using System.Text.Json;
using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>
/// 主题切换测试：验证通过设置窗口"外观"段切换 Light/Dark 主题后，
/// 即时应用并持久化到 user-settings.json。
/// 对应 0527.exe 的主题切换契约（ThemeService.Apply + Persist）。
/// </summary>
[Collection("Host")]
public class ThemeTests
{
    private readonly HostAppFixture _fixture;

    public ThemeTests(HostAppFixture fixture) => _fixture = fixture;

    /// <summary>切换到浅色主题 → user-settings.json 应记录 theme=Light。</summary>
    [Fact]
    public void Theme_SwitchToLight_PersistsToUserSettings()
    {
        // 1. 从登录窗口打开设置
        var settingsWindow = OpenSettingsWindow();

        // 2. 切换到"外观"段（ListBoxItem "外观"）
        SwitchToAppearanceSection(settingsWindow);

        // 3. 点击"浅色 (Light)" RadioButton
        ClickThemeRadioButton(settingsWindow, "浅色 (Light)");

        // 4. 验证 user-settings.json 持久化为 Light
        var theme = ReadPersistedTheme();
        theme.Should().Be("Light", "切换到浅色后应持久化 theme=Light");

        // 5. 恢复为深色（避免影响后续测试）
        ClickThemeRadioButton(settingsWindow, "深色 (Dark)");
    }

    /// <summary>切换到深色主题 → user-settings.json 应记录 theme=Dark。</summary>
    [Fact]
    public void Theme_SwitchToDark_PersistsToUserSettings()
    {
        var settingsWindow = OpenSettingsWindow();
        SwitchToAppearanceSection(settingsWindow);

        // 先切到浅色，再切回深色，验证双向都能持久化
        ClickThemeRadioButton(settingsWindow, "浅色 (Light)");
        ClickThemeRadioButton(settingsWindow, "深色 (Dark)");

        var theme = ReadPersistedTheme();
        theme.Should().Be("Dark", "切换到深色后应持久化 theme=Dark");
    }

    /// <summary>打开设置窗口：点击登录页"设置"按钮，等待设置窗口出现。</summary>
    /// <remarks>
    /// 用 <see cref="UiTestHelpers.ClickUntilWindowAppears"/> 统一的重试策略：
    /// 先检查窗口已存在（避免堆叠多窗口）+ 每次点击前确保 LoginWindow 前台 + 重试 3 次。
    /// </remarks>
    private Window OpenSettingsWindow()
    {
        _fixture.CloseChildWindows();
        _fixture.EnsureLoginWindowForeground();

        // 每次轮询都刷新 LoginWindow，避免上一轮 SettingsWindow 生命周期留下陈旧 UIA 子树。
        var settingsBtn = UiTestHelpers.WaitFor(() =>
            UiTestHelpers.FindOpenSettingsButton(_fixture),
            TimeSpan.FromSeconds(10));
        settingsBtn.Should().NotBeNull("设置按钮应存在");

        // Click + 重试（先检查已存在避免堆叠多窗口，每次点击前确保前台）
        var settingsWindow = UiTestHelpers.ClickUntilWindowAppears(_fixture, settingsBtn!, "SettingsWindow");
        settingsWindow.Should().NotBeNull("点击设置后应出现设置窗口");
        return settingsWindow!;
    }

    /// <summary>切换到"外观"段：点击 ListBox 中的"外观"项（CurrentSectionIndex=4）。</summary>
    private void SwitchToAppearanceSection(Window settingsWindow)
    {
        // ListBoxItem 继承自 SelectionItemAutomationElement，不在 AutomationElement 继承链里，
        // `as ListBoxItem` 必返回 null。用 AsListBoxItem() 扩展方法包装。
        var appearanceItem = UiTestHelpers.WaitFor(() =>
            settingsWindow.FindFirstDescendant(settingsWindow.Automation.ConditionFactory.ByName("外观"))?.AsListBoxItem(),
            TimeSpan.FromSeconds(3));
        appearanceItem.Should().NotBeNull("应存在'外观'段导航项");
        // WPF UI ListBoxItem 不暴露 SelectionItemPattern（Select() 会抛 PatternNotSupportedException），
        // 改用 Click()。Click 前确保 SettingsWindow 在前台，否则鼠标坐标可能落到其他窗口。
        _fixture.EnsureWindowForeground(settingsWindow);
        appearanceItem!.Click();
        // 等待 ContentControl 切换 DataTemplate，RadioButton 出现
        System.Threading.Thread.Sleep(300);
    }

    /// <summary>选中指定主题 RadioButton（按 Content 文本定位）。</summary>
    /// <remarks>
    /// FlaUI 4.0 的 <c>RadioButton</c> 不继承 <c>Button</c>（继承自 <c>AutomationElement</c>），
    /// 没有 <c>Invoke()</c> 方法，<c>as RadioButton</c> 也必返回 null（不在继承链里）。
    /// 用 <c>AsRadioButton()</c> 扩展方法包装后，直接设置 <c>IsChecked = true</c> 最可靠：
    /// 不依赖窗口前台（避免 <c>SetFocus</c>/<c>Click</c> 鼠标坐标落空），
    /// 也不依赖 <c>InvokePattern</c>（WPF RadioButton 默认不暴露 InvokePattern）。
    /// 设置 <c>IsChecked</c> 会触发 WPF <c>Checked</c> 事件 → ViewModel 主题切换 → ThemeService.Apply。
    /// </remarks>
    private static void ClickThemeRadioButton(Window settingsWindow, string radioContent)
    {
        var radio = UiTestHelpers.WaitFor(() =>
            settingsWindow.FindFirstDescendant(
                settingsWindow.Automation.ConditionFactory.ByName(radioContent))?.AsRadioButton(),
            TimeSpan.FromSeconds(3));
        radio.Should().NotBeNull($"应存在 RadioButton '{radioContent}'");
        radio!.IsChecked = true;
        // 主题应用 + 持久化是同步的（ThemeService.Apply 内部调 Persist），短暂等待文件写入完成
        System.Threading.Thread.Sleep(300);
    }

    /// <summary>读取 user-settings.json 中的 theme 字段。</summary>
    private string? ReadPersistedTheme()
    {
        var path = Path.Combine(_fixture.HostExeDirectory, "user-settings.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("theme", out var t) ? t.GetString() : null;
    }
}
