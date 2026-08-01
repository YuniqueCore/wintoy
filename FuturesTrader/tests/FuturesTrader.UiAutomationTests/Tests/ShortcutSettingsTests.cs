using System.ComponentModel;
using System.Runtime.InteropServices;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FluentAssertions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

[Collection("Host")]
public class ShortcutSettingsTests
{
    private readonly HostAppFixture _fixture;

    public ShortcutSettingsTests(HostAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void Shortcut_editor_records_rejects_conflicts_and_resets_defaults()
    {
        var settings = OpenSettingsWindow();
        try
        {
            SelectNavigationItem(settings, "快捷键");
            var resetAll = UiTestHelpers.WaitFor(() =>
                settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByName("恢复全部默认"))?.AsButton(),
                TimeSpan.FromSeconds(5));
            resetAll.Should().NotBeNull("快捷键设置应提供全部重置");
            resetAll!.Invoke();

            var recordButtons = UiTestHelpers.WaitFor(() =>
            {
                var buttons = settings.FindAllDescendants(
                    _fixture.Automation.ConditionFactory.ByName("录制"));
                return buttons.Length >= 7 ? buttons : null;
            }, TimeSpan.FromSeconds(5));
            recordButtons.Should().NotBeNull("七个快捷键动作都应提供录制按钮");
            recordButtons![0].AsButton().Invoke();
            _fixture.EnsureWindowForeground(settings);

            PressWindowKey(settings, VirtualKeyShort.KEY_W);
            UiTestHelpers.WaitTrue(() =>
            {
                var message = settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("ShortcutValidationMessage"));
                return message?.Name.Contains("强制全撤", StringComparison.Ordinal) == true;
            }, TimeSpan.FromSeconds(3)).Should().BeTrue("录制已占用的 W 时应显示明确冲突归属");

            PressWindowKey(settings, VirtualKeyShort.F12);
            UiTestHelpers.WaitTrue(() => settings.FindAllDescendants(
                    _fixture.Automation.ConditionFactory.ByName("F12")).Length > 0,
                TimeSpan.FromSeconds(3)).Should().BeTrue("无冲突键应被录制并显示");

            SaveConfiguration(settings);
            resetAll = settings.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByName("恢复全部默认"))?.AsButton();
            resetAll.Should().NotBeNull();
            resetAll!.Invoke();
            SaveConfiguration(settings);
            settings.FindAllDescendants(_fixture.Automation.ConditionFactory.ByName("Space"))
                .Should().NotBeEmpty("恢复全部默认后 Space 应重新显示为选择性全撤键");
        }
        finally
        {
            try { settings.Close(); } catch { }
            _fixture.EnsureLoginWindowForeground();
        }
    }

    private Window OpenSettingsWindow()
    {
        _fixture.CloseChildWindows();
        _fixture.EnsureLoginWindowForeground();
        var button = UiTestHelpers.WaitFor(() => UiTestHelpers.FindOpenSettingsButton(_fixture),
            TimeSpan.FromSeconds(10));
        button.Should().NotBeNull();
        return UiTestHelpers.ClickUntilWindowAppears(_fixture, button!, "SettingsWindow")
            ?? throw new Xunit.Sdk.XunitException("点击设置后未出现设置窗口");
    }

    private void SelectNavigationItem(Window settings, string name)
    {
        var item = UiTestHelpers.WaitFor(() =>
        {
            var current = settings.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName(name));
            while (current is not null && current.ControlType != ControlType.ListItem) current = current.Parent;
            return current?.AsListBoxItem();
        }, TimeSpan.FromSeconds(5));
        item.Should().NotBeNull();
        item!.Select();
    }

    private void SaveConfiguration(Window settings)
    {
        var save = UiTestHelpers.WaitFor(() =>
            settings.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("保存配置"))?.AsButton(),
            TimeSpan.FromSeconds(5));
        save.Should().NotBeNull();
        UiTestHelpers.WaitTrue(() => save!.IsEnabled, TimeSpan.FromSeconds(5)).Should().BeTrue();
        save!.Invoke();
        Thread.Sleep(300);
    }

    private static void PressWindowKey(Window window, VirtualKeyShort key)
    {
        try
        {
            Keyboard.Press(key);
            return;
        }
        catch (Win32Exception)
        {
            // 长套件所在的自动化桌面可能由 UIPI 禁止 SendInput。
        }

        var handle = new IntPtr(window.FrameworkAutomationElement.NativeWindowHandle.Value);
        PostMessage(handle, WmKeyDown, new IntPtr((int)key), IntPtr.Zero).Should().BeTrue();
        PostMessage(handle, WmKeyUp, new IntPtr((int)key), IntPtr.Zero).Should().BeTrue();
        Thread.Sleep(80);
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
