using System.IO;
using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>
/// 窗口管理测试：验证设置窗口的打开/关闭，以及单例守卫拦截二次启动。
/// 对应 0527.exe 的窗口管理契约（SettingsWindow 瞬态 + SingleInstanceGuard）。
/// </summary>
[Collection("Host")]
public class WindowManagementTests
{
    private readonly HostAppFixture _fixture;

    public WindowManagementTests(HostAppFixture fixture) => _fixture = fixture;

    /// <summary>点击"设置"按钮 → 应打开 AutomationId="SettingsWindow" 的新窗口。</summary>
    /// <remarks>
    /// 交互可靠性策略（实测必须三管齐下，缺一不可）：
    /// 1. <see cref="HostAppFixture.CloseChildWindows"/>：关闭残留 SettingsWindow，避免遮挡 LoginWindow 的设置按钮
    /// 2. <see cref="HostAppFixture.EnsureLoginWindowForeground"/>：AttachThreadInput 强制 LoginWindow 到前台
    /// 3. <see cref="UiTestHelpers.ClickUntilWindowAppears"/>：先检查已存在再点击 + 重试，避免堆叠多窗口
    /// </remarks>
    [Fact]
    public void Settings_OpenFromLogin_ShowsSettingsWindow()
    {
        var loginWindow = _fixture.LoginWindow!;
        _fixture.CloseChildWindows();
        _fixture.EnsureLoginWindowForeground();

        // 等待"设置"按钮在 UIA 树中出现（FluentWindow 内容渲染有延迟，单测隔离运行时尤其明显）
        var settingsBtn = UiTestHelpers.WaitFor(() =>
            loginWindow.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("设置"))?.AsButton(),
            TimeSpan.FromSeconds(10));
        settingsBtn.Should().NotBeNull("应找到设置按钮（Content='设置'）");

        // Click + 重试（先检查已存在避免堆叠多窗口，每次点击前确保前台）
        var settingsWindow = UiTestHelpers.ClickUntilWindowAppears(_fixture, settingsBtn!, "SettingsWindow");
        settingsWindow.Should().NotBeNull("应打开设置窗口（AutomationId=SettingsWindow）");
    }

    /// <summary>关闭设置窗口 → 应回到登录窗口（登录窗口仍可见）。</summary>
    [Fact]
    public void Settings_Close_ReturnsToLoginWindow()
    {
        // 1. 打开设置窗口（复用 OpenFromLogin 的可靠性策略）
        var loginWindow = _fixture.LoginWindow!;
        _fixture.CloseChildWindows();
        _fixture.EnsureLoginWindowForeground();
        var settingsBtn = UiTestHelpers.WaitFor(() =>
            loginWindow.FindFirstDescendant(_fixture.Automation.ConditionFactory.ByName("设置"))?.AsButton(),
            TimeSpan.FromSeconds(10));
        settingsBtn.Should().NotBeNull("应找到设置按钮");
        var settingsWindow = UiTestHelpers.ClickUntilWindowAppears(_fixture, settingsBtn!, "SettingsWindow");
        settingsWindow.Should().NotBeNull();

        // 2. 关闭设置窗口（Close 等价于点 X）
        settingsWindow!.Close();
        // 关闭 owned window 后 Windows 不一定把前台还给 LoginWindow，主动恢复避免"回到桌面"
        _fixture.EnsureLoginWindowForeground();

        // 3. 等待设置窗口消失
        var gone = UiTestHelpers.WaitTrue(() =>
            _fixture.FindWindowByAutomationId("SettingsWindow", TimeSpan.FromSeconds(1)) is null,
            TimeSpan.FromSeconds(3));
        gone.Should().BeTrue("设置窗口应在 Close 后消失");

        // 4. 登录窗口应仍可见
        loginWindow.IsAvailable.Should().BeTrue("关闭设置后登录窗口应仍可用");
    }

    /// <summary>单例守卫：已运行实例时，二次启动应被拦截（新进程退出）。</summary>
    [Fact]
    public async Task SingleInstance_SecondLaunch_IsBlocked()
    {
        // _fixture 已启动一个 Host 实例，再启动第二个应被单例守卫拦截
        var hostExe = Path.Combine(_fixture.HostExeDirectory, "FuturesTrader.Host.exe");
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = hostExe,
            WorkingDirectory = _fixture.HostExeDirectory,
            UseShellExecute = false,
        };
        var second = System.Diagnostics.Process.Start(psi);
        second.Should().NotBeNull("第二个进程应能启动（随后被守卫拦截退出）");

        // 单例守卫通过命名事件检测，检测到已运行实例后立即 Shutdown()
        // 给守卫 3 秒时间检测并退出
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await second!.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            // 超时未退出 → 守卫未生效，清理后失败
            second.Kill();
            throw new Xunit.Sdk.XunitException("第二个实例未在 5 秒内退出，单例守卫可能未生效");
        }

        second.HasExited.Should().BeTrue("第二个实例应被单例守卫拦截并退出");
    }
}
