using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>
/// 登录窗口 UI 元素测试：验证行情地址表、账号列表、密码框、登录/设置按钮均存在且可交互。
/// 对应 0527.exe 登录页的核心控件 1:1 复刻契约。
/// <para>
/// appsettings.json 已是 Mock 模式（Provider=Mock），登录页会注入测试账号 000102，
/// 行情地址从 data/HQAddress.xml 加载。
/// </para>
/// </summary>
[Collection("Host")]
public class LoginWindowTests
{
    private readonly HostAppFixture _fixture;

    public LoginWindowTests(HostAppFixture fixture) => _fixture = fixture;

    /// <summary>行情地址 DataGrid 应存在且有数据行（从 HQAddress.xml 加载）。</summary>
    /// <remarks>
    /// WPF DataGrid 在 UIA3 暴露为 <see cref="ControlType.Table"/>（而非 DataGrid），
    /// 按 ControlType.DataGrid 查找会失败。改用 <c>x:Name</c> → <c>ByAutomationId</c> 定位最稳健。
    /// 数据行在 UIA3 是 <see cref="ControlType.DataItem"/>。
    /// </remarks>
    [Fact]
    public void LoginWindow_MarketAddressGrid_HasData()
    {
        var window = _fixture.LoginWindow!;
        var grid = UiTestHelpers.WaitFor(() =>
            window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("MarketAddressGrid")),
            TimeSpan.FromSeconds(10));

        grid.Should().NotBeNull("行情地址 DataGrid 应存在（x:Name=MarketAddressGrid）");

        // WPF DataGrid 行在 UIA3 是 ControlType.DataItem（表头是 Header，不混入）
        var rows = grid!.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByControlType(ControlType.DataItem));
        rows.Length.Should().BeGreaterThan(0, "应从 HQAddress.xml 加载至少 1 个行情地址");
    }

    /// <summary>交易账号 ListBox 应存在且有数据项（从 Users.xml 加载）。</summary>
    /// <remarks>
    /// WPF ListBox 的 <c>x:Name</c> 在 UIA3 中有时不映射为 AutomationId（与 DataGrid 行为不一致），
    /// 故 <c>ByAutomationId</c> 找不到时回退到 <c>ByControlType(ControlType.List)</c>。
    /// 不 cast 成 <see cref="ListBox"/>：避免 ControlType 不匹配导致 <c>as</c> 返回 null，
    /// 改用 <c>FindAllDescendants(ListItem)</c> 验证项数。
    /// </remarks>
    [Fact]
    public void LoginWindow_AccountList_HasItems()
    {
        var window = _fixture.LoginWindow!;
        var list = UiTestHelpers.WaitFor(() =>
            window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("AccountList"))
                ?? window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByControlType(ControlType.List)),
            TimeSpan.FromSeconds(10));

        list.Should().NotBeNull("交易账号 ListBox 应存在");
        var items = list!.FindAllDescendants(
            _fixture.Automation.ConditionFactory.ByControlType(ControlType.ListItem));
        items.Length.Should().BeGreaterThan(0, "应从 Users.xml 加载至少 1 个账号");
    }

    /// <summary>密码框应存在且可通过 AutomationId 定位（x:Name="PasswordBox"）。</summary>
    [Fact]
    public void LoginWindow_PasswordBox_IsAccessible()
    {
        var window = _fixture.LoginWindow!;
        var passwordBox = window.FindFirstDescendant(
            _fixture.Automation.ConditionFactory.ByAutomationId("PasswordBox"));

        passwordBox.Should().NotBeNull("密码框应存在（AutomationId=PasswordBox）");
        passwordBox!.ControlType.Should().Be(ControlType.Edit, "WPF UI PasswordBox 在 UIA 中映射为 Edit");
    }

    /// <summary>登录按钮应存在且文本为"登录"。</summary>
    [Fact]
    public void LoginWindow_LoginButton_Exists()
    {
        var window = _fixture.LoginWindow!;
        var btn = window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("登录"));

        btn.Should().NotBeNull("登录按钮应存在（Content='登录'）");
        btn!.ControlType.Should().Be(ControlType.Button);
    }

    /// <summary>设置按钮应存在且文本为"设置"。</summary>
    [Fact]
    public void LoginWindow_SettingsButton_Exists()
    {
        var window = _fixture.LoginWindow!;
        var btn = window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("设置"));

        btn.Should().NotBeNull("设置按钮应存在（Content='设置'）");
        btn!.ControlType.Should().Be(ControlType.Button);
    }

    /// <summary>输入密码后，登录按钮应变为启用状态（CanLogin 依赖密码非空）。</summary>
    /// <remarks>
    /// WPF <c>PasswordBox</c> 在 UIA3 虽是 <see cref="ControlType.Edit"/>，但因安全限制不支持
    /// <c>ValuePattern</c>，FlaUI 工厂将其包装为 <c>PasswordBox</c> 类而非 <c>TextBox</c>，
    /// 故 <c>as TextBox</c> 必返回 null。正确做法：<c>Focus</c> + <c>Keyboard.Type</c> 模拟真实键盘输入，
    /// 触发 WPF 的 <c>PasswordChanged</c> 事件 → ViewModel 密码更新 → <c>CanLogin</c> 重算。
    /// </remarks>
    [Fact]
    public void LoginWindow_LoginButton_EnablesAfterPasswordEntered()
    {
        var window = _fixture.LoginWindow!;

        // 0. 清理前置测试可能残留的 SettingsWindow，并确保 LoginWindow 在前台
        _fixture.CloseChildWindows();
        _fixture.EnsureLoginWindowForeground();

        // 1. 定位密码框 + 登录按钮（等待 UIA 树就绪，FluentWindow 内容渲染有延迟）
        var passwordBox = UiTestHelpers.WaitFor(() =>
            window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByAutomationId("PasswordBox")),
            TimeSpan.FromSeconds(10));
        passwordBox.Should().NotBeNull("密码框应存在（x:Name=PasswordBox）");

        var loginBtn = UiTestHelpers.WaitFor(() =>
            window.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("登录")),
            TimeSpan.FromSeconds(10));
        loginBtn.Should().NotBeNull("登录按钮应存在");

        // 2. 输入密码前：登录按钮应禁用（无密码时 CanLogin=false）
        loginBtn!.IsEnabled.Should().BeFalse("无密码时登录按钮应禁用");

        // 3. 输入密码（Click 聚焦 + Keyboard.Type），重试直到登录按钮启用或达 3 次上限。
        //    首次 Click 可能因窗口前台切换时序问题未真正聚焦到 PasswordBox 内部 TextBox，
        //    重试可显著提升可靠性。
        var enabled = false;
        for (var attempt = 0; attempt < 3 && !enabled; attempt++)
        {
            _fixture.EnsureLoginWindowForeground();
            passwordBox!.Click();
            System.Threading.Thread.Sleep(150);
            FlaUI.Core.Input.Keyboard.Type("258147");
            enabled = UiTestHelpers.WaitTrue(() => loginBtn.IsEnabled, TimeSpan.FromSeconds(2));
        }
        enabled.Should().BeTrue("输入密码后登录按钮应启用（CanLogin 依赖密码非空）");
    }
}
