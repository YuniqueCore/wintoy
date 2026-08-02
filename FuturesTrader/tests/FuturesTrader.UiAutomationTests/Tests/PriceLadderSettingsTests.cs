using FlaUI.Core.AutomationElements;
using System.Runtime.InteropServices;
using FlaUI.Core.Definitions;
using FluentAssertions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>合约窗口共享显示配置与窗口分组列表的 Settings 实机回归。</summary>
[Collection("Host")]
public sealed class PriceLadderSettingsTests
{
    private readonly HostAppFixture _fixture;

    public PriceLadderSettingsTests(HostAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void Window_section_owns_one_shared_ladder_configuration_and_group_rows_show_titles()
    {
        var floating = LoginToFloatingWindow();
        Window? settings = null;
        try
        {
            settings = OpenSettingsWindow(floating);
            SelectNavigationItem(settings, "Window 段");
            var rowHeightEditor = WaitForNumberBoxEditor(settings, "SettingsContractRowHeight");
            var askEditor = WaitForNumberBoxEditor(settings, "SettingsAskQuoteRowCount");
            var bidEditor = WaitForNumberBoxEditor(settings, "SettingsBidQuoteRowCount");
            var automaticWhite = UiTestHelpers.WaitFor(() =>
                settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("SettingsAutomaticWhiteGrid")),
                TimeSpan.FromSeconds(10));

            rowHeightEditor.Should().NotBeNull("Window 段必须提供所有合约窗口共用的格高");
            askEditor.Should().NotBeNull("Window 段必须提供所有合约窗口共用的空区格数");
            bidEditor.Should().NotBeNull("Window 段必须提供所有合约窗口共用的多区格数");
            automaticWhite.Should().NotBeNull("白格必须显示为自动派生，不能提供错误的手动数量");
            automaticWhite!.Name.Should().StartWith("自动");

            int.TryParse(rowHeightEditor!.AsTextBox().Text, out var rowHeight).Should().BeTrue();
            int.TryParse(askEditor!.AsTextBox().Text, out var askRows).Should().BeTrue();
            int.TryParse(bidEditor!.AsTextBox().Text, out var bidRows).Should().BeTrue();
            rowHeight.Should().BeInRange(10, 32);
            askRows.Should().BeInRange(5, 100);
            bidRows.Should().BeInRange(5, 100);

            SelectNavigationItem(settings, "外观");
            var dark = UiTestHelpers.WaitFor(() => settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByName("深色 (Dark)"))?.AsRadioButton(),
                TimeSpan.FromSeconds(5));
            dark.Should().NotBeNull();
            dark!.IsChecked = true;

            SelectNavigationItem(settings, "窗口分组");
            settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("SettingsAskQuoteRowCount"))
                .Should().BeNull("窗口列表不能为每个窗口重复显示共享配置");
            var list = UiTestHelpers.WaitFor(() => settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("SettingsWindowGroupList")),
                TimeSpan.FromSeconds(5));
            list.Should().NotBeNull();
            var selectedRow = list!.FindAllChildren()
                .FirstOrDefault(element => element.ControlType == ControlType.ListItem)?.AsListBoxItem();
            selectedRow.Should().NotBeNull();
            selectedRow!.Select();
            selectedRow.IsSelected.Should().BeTrue("深色模式下窗口行必须保持可选择状态");
            var fullTitle = UiTestHelpers.WaitFor(() =>
            {
                var title = list.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("SettingsContractWindowTitle"))?.Name;
                return !string.IsNullOrWhiteSpace(title) && title.Contains(" - ", StringComparison.Ordinal)
                    ? title
                    : null;
            }, TimeSpan.FromSeconds(10));
            fullTitle.Should().NotBeNullOrWhiteSpace(
                "已登录时窗口行必须与合约 TitleBar 一样展示“名称 - 代码”，不能只显示代码");

            settings.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByAutomationId("AbModeSwitcher"))
                .Should().BeNull("A/B 只能保留在 Floating Bottom Window，不应在 Settings 重复出现");
        }
        finally
        {
            try { settings?.Close(); } catch { }
            _fixture.CloseChildWindows();
            Logout(floating);
        }
    }

    private Window LoginToFloatingWindow()
    {
        var login = _fixture.RefreshLoginWindow()
            ?? throw new Xunit.Sdk.XunitException("登录窗口不存在");
        _fixture.EnsureLoginWindowForeground();
        var password = login.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PasswordBox"));
        var loginButton = login.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByName("登录"))?.AsButton();
        password.Should().NotBeNull();
        loginButton.Should().NotBeNull();
        password!.AsTextBox().Text = "mock-password";
        UiTestHelpers.WaitTrue(() => loginButton!.IsEnabled, TimeSpan.FromSeconds(5)).Should().BeTrue();
        loginButton!.Invoke();
        return _fixture.FindWindowByAutomationId("FloatingMainWindow", TimeSpan.FromSeconds(15))
            ?? throw new Xunit.Sdk.XunitException("Mock 登录后未出现浮动工具栏");
    }

    private Window OpenSettingsWindow(Window floating)
    {
        _fixture.EnsureWindowForeground(floating);
        var button = UiTestHelpers.WaitFor(() => floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("FloatingOpenSettingsButton"))?.AsButton(),
            TimeSpan.FromSeconds(10));
        button.Should().NotBeNull();
        button!.Invoke();
        return UiTestHelpers.WaitFor(() =>
        {
            try
            {
                return _fixture.FindWindowByAutomationId("SettingsWindow", TimeSpan.FromSeconds(1));
            }
            catch (COMException)
            {
                return null;
            }
        }, TimeSpan.FromSeconds(20))
            ?? throw new Xunit.Sdk.XunitException("从 Floating Bottom Window 点击设置后未出现设置窗口");
    }

    private void Logout(Window floating)
    {
        try
        {
            _fixture.EnsureWindowForeground(floating);
            floating.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId("LogoutButton"))?.AsButton().Invoke();
            _ = UiTestHelpers.WaitFor(_fixture.RefreshLoginWindow, TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Fixture 最终会终止 Host；清理失败不覆盖主断言。
        }
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

    private AutomationElement? WaitForNumberBoxEditor(Window settings, string automationId) =>
        UiTestHelpers.WaitFor(() =>
        {
            var numberBox = settings.FindFirstDescendant(
                _fixture.Automation.ConditionFactory.ByAutomationId(automationId));
            if (numberBox is null) return null;
            return numberBox.ControlType == ControlType.Edit
                ? numberBox
                : numberBox.FindFirstDescendant(
                    _fixture.Automation.ConditionFactory.ByControlType(ControlType.Edit));
        }, TimeSpan.FromSeconds(10));
}
