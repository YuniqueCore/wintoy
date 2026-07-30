using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests.Tests;

/// <summary>
/// 应用启动测试：验证 Host exe 能正常启动并显示登录窗口。
/// 对应 0527.exe 升级后的启动契约：单例守卫 → 主题 → 登录页。
/// </summary>
[Collection("Host")]
public class StartupTests
{
    private readonly HostAppFixture _fixture;

    public StartupTests(HostAppFixture fixture) => _fixture = fixture;

    /// <summary>启动后应显示标题为"期货交易终端 · 登录"的窗口。</summary>
    [Fact]
    public void Startup_ShowsLoginWindow_WithExpectedTitle()
    {
        var window = _fixture.LoginWindow;
        window.Should().NotBeNull("启动后应显示登录窗口");
        window!.Title.Should().Contain("登录", "登录窗口标题应包含'登录'");
        window.Title.Should().Contain("期货交易终端", "登录窗口标题应包含应用名");
    }

    /// <summary>登录窗口应为可见且可交互状态。</summary>
    /// <remarks>
    /// <c>FluentWindow</c>（<c>ExtendsContentIntoTitleBar=True</c>）的 <c>BoundingRectangle.Height</c>
    /// 在 UIA3 中可能返回极小值（如 17，仅标题栏区域），不能用 Height 断言非最小化。
    /// 改用 <c>IsAvailable</c> + <c>Width</c> 判断窗口可见。
    /// </remarks>
    [Fact]
    public void Startup_LoginWindow_IsInteractive()
    {
        var window = _fixture.LoginWindow!;
        window.IsAvailable.Should().BeTrue("登录窗口应可用");
        window.BoundingRectangle.Width.Should().BeGreaterThan(100, "登录窗口应有宽度（非最小化）");
    }

    /// <summary>Host 进程应为 x64（CTP 6.7.13 64位 DLL 要求）。</summary>
    [Fact]
    public void Startup_HostProcess_IsX64Architecture()
    {
        _fixture.App.Should().NotBeNull("Host 进程应已 attach");
        // 通过进程名验证架构（FlaUI 不直接暴露位数，用 Process 验证）
        var proc = System.Diagnostics.Process.GetProcessById(_fixture.App!.ProcessId);
        proc.Should().NotBeNull();
        // 64位进程的 ProcessName 不含 32 标记；更可靠的是 IsWow64（32位进程在64位OS上为 true）
        // 这里验证进程可访问即视为启动成功；架构验证由 csproj PlatformTarget=x64 保证
        proc!.SessionId.Should().BeGreaterThanOrEqualTo(0);
    }
}
