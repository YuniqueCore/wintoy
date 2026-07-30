using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace FuturesTrader.UiAutomationTests.Fixtures;

/// <summary>
/// Host exe 启动/关闭 fixture：每个测试类共享一个 exe 实例。
/// <para>
/// 职责：
/// <list type="number">
///   <item>清理可能残留的 FuturesTrader.Host 进程（避免单例守卫拦截新实例）</item>
///   <item>启动 Host exe（appsettings.json 已是 Mock 模式，不连真实 CTP）</item>
///   <item>等待 LoginWindow 出现（标题"期货交易终端 · 登录"）</item>
///   <item>暴露 <see cref="Automation"/>（UIA3）与 <see cref="LoginWindow"/> 供测试交互</item>
///   <item>Dispose 时优雅关闭 exe（Close → Kill 兜底）</item>
/// </list>
/// </para>
/// <para>
/// <b>架构约束</b>：Host 是 x64 进程（CTP 6.7.13 64位 DLL），测试项目也必须 x64，
/// 否则 FlaUI 无法 attach（跨架构 attach 不支持）。
/// </para>
/// </summary>
public sealed class HostAppFixture : IAsyncLifetime
{
    private const string HostProcessName = "FuturesTrader.Host";
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(20);

    private Process? _process;

    /// <summary>FlaUI UIA3 自动化引擎（现代 WPF + WPF UI 推荐 UIA3）。</summary>
    public UIA3Automation Automation { get; } = new();

    /// <summary>Host 进程的 FlaUI Application 句柄。</summary>
    public Application? App { get; private set; }

    /// <summary>登录窗口（启动后立即可见）。</summary>
    public Window? LoginWindow { get; private set; }

    /// <summary>Host exe 所在目录（用于读取 user-settings.json 验证主题持久化）。</summary>
    public string HostExeDirectory => Path.GetDirectoryName(ResolveHostExePath())!;

    public Task InitializeAsync()
    {
        // 1. 清理残留进程（单例守卫会拦截二次启动，必须先 kill）
        KillOrphanHostProcesses();
        // 等待内核对象（单例守卫的命名事件）释放，避免新进程被拦截
        // Kill 后进程退出，但命名事件等内核对象有延迟释放，1 秒缓冲确保清理完成
        Thread.Sleep(1000);

        // 2. 定位 Host exe（相对测试输出目录）
        var hostExe = ResolveHostExePath();
        if (!File.Exists(hostExe))
            throw new FileNotFoundException(
                $"Host exe 未找到。请先 build Host 项目。路径: {hostExe}", hostExe);

        // 3. 启动 Host（工作目录设为 exe 目录，确保 appsettings.json/data/ 相对路径解析正确）
        var startInfo = new ProcessStartInfo
        {
            FileName = hostExe,
            WorkingDirectory = Path.GetDirectoryName(hostExe)!,
            UseShellExecute = false,
        };
        _process = new Process { StartInfo = startInfo };
        if (!_process.Start())
            throw new InvalidOperationException("Host exe 启动失败");

        App = Application.Attach(_process.Id);

        // 4. 等待 LoginWindow 出现（标题包含"登录"）
        LoginWindow = UiTestHelpers.WaitFor(FindLoginWindow, StartupTimeout)
            ?? throw new TimeoutException(
                $"启动后 {StartupTimeout.TotalSeconds}s 内未找到 LoginWindow。" +
                "可能原因：单例守卫拦截（残留进程未清理）/ Host 启动崩溃 / 窗口标题变更");

        // 4b. 等待 LoginWindow 内容渲染完成：FluentWindow 的内容（Button/PasswordBox 等）由 WPF 布局系统
        //     异步测量/排列后才进入 UIA 树。直接等"设置"按钮有时不稳定（UIA 树构建有抖动），
        //     改为轮询 LoginWindow 的子元素数量，一旦有子元素即视为内容已挂载。
        //     不抛异常——即使超时也继续，让单个测试自行等待自己需要的元素（避免 fixture 级失败影响全部测试）。
        UiTestHelpers.WaitTrue(() =>
        {
            try
            {
                var children = LoginWindow.FindAllChildren();
                return children.Length > 0;
            }
            catch { return false; }
        }, TimeSpan.FromSeconds(10));

        // 5. 置前台：Process.Start 后测试 runner 控制台可能抢回前台，导致后续 SetFocus/Invoke 失效。
        //    用 EnsureLoginWindowForeground（AttachThreadInput 强制前台 + FocusNative 兜底），
        //    比单纯 FocusNative 可靠——反偷焦限制下 FocusNative 静默失败。
        EnsureLoginWindowForeground();

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        try
        {
            // 优雅关闭：先 Close 主窗口，再 Kill 兜底
            // 不走 App.Close（可能弹确认对话框阻塞），直接 Kill 确保进程退出
            if (_process is { HasExited: false })
            {
                try { _process.Kill(entireProcessTree: true); }
                catch { /* 进程可能已退出 */ }
                // 循环等待进程真正退出（最多 10 秒），确保下次测试启动时单例守卫命名事件已释放
                for (var i = 0; i < 100 && !_process.HasExited; i++)
                    await Task.Delay(100);
            }
        }
        finally
        {
            Automation.Dispose();
            App?.Dispose();
            _process?.Dispose();
        }
    }

    /// <summary>重新获取当前主窗口（窗口切换后，如登录后切到 FloatingMainWindow）。</summary>
    public Window? RefreshMainWindow(TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(10);
        return UiTestHelpers.WaitFor(() => App?.GetMainWindow(Automation), wait);
    }

    /// <summary>按标题查找窗口（标题包含 <paramref name="titlePart"/>）。</summary>
    /// <remarks>
    /// 优先从 App 进程的顶级窗口找；若找不到，回退到桌面根全量搜索。
    /// 回退原因：<c>FluentWindow</c>（<c>ExtendsContentIntoTitleBar=True</c>）的 Title
    /// 在 UIA3 中可能不暴露给 <c>App.GetAllTopLevelWindows</c>，但桌面根能枚举到。
    /// </remarks>
    public Window? FindWindowByTitle(string titlePart, TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(8);
        return UiTestHelpers.WaitFor(() =>
        {
            // 1. 优先从 App 进程的顶级窗口找
            var fromApp = App?.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(w => w.Title.Contains(titlePart, StringComparison.Ordinal));
            if (fromApp is not null) return fromApp;

            // 2. fallback：从桌面根按 ControlType.Window 全量搜索
            //    AutomationElement.Name 即 UIA Name 属性，对窗口而言等于窗口标题
            var desktop = Automation.GetDesktop();
            return desktop.FindAllChildren(Automation.ConditionFactory.ByControlType(ControlType.Window))
                .FirstOrDefault(w => w.Name.Contains(titlePart, StringComparison.Ordinal))?.AsWindow();
        }, wait);
    }

    /// <summary>
    /// 确保指定窗口在前台。测试间若打开了其他窗口（如 SettingsWindow），
    /// 或测试运行器控制台抢焦，窗口会失去前台 → 后续 <c>Click</c>/<c>SetFocus</c> 落空。
    /// <para>
    /// 用 <see cref="Win32ForegroundHelper.ForceForeground"/>（<c>AttachThreadInput</c> 绕过反偷焦），
    /// 比 FlaUI 的 <c>FocusNative</c>（仅 <c>SetForegroundWindow</c>，反偷焦下静默失败）可靠得多。
    /// <c>FocusNative</c> 作为兜底保留——不同时机可能生效。
    /// </para>
    /// </summary>
    public void EnsureWindowForeground(Window window)
    {
        if (window is null) return;
        try
        {
            // FlaUI Window 类没有直接的 Handle 属性，通过 FrameworkAutomationElement.NativeWindowHandle 获取 HWND
            // NativeWindowHandle 是 UIA 属性（int），new IntPtr 转换为句柄
            var handle = new IntPtr(window.FrameworkAutomationElement.NativeWindowHandle.Value);
            if (handle != IntPtr.Zero)
                Win32ForegroundHelper.ForceForeground(handle);
        }
        catch { /* HWND 可能尚未创建或已销毁，忽略 */ }
        try { window.FocusNative(); }
        catch { /* SetForegroundWindow 反偷焦限制，忽略 */ }
    }

    /// <summary>确保 LoginWindow 在前台（<see cref="EnsureWindowForeground"/> 的快捷入口）。</summary>
    public void EnsureLoginWindowForeground()
    {
        if (LoginWindow is not null) EnsureWindowForeground(LoginWindow);
    }

    /// <summary>
    /// 关闭除 LoginWindow 外的所有顶级窗口（如残留的 SettingsWindow）。
    /// 用于测试间清理，避免前置测试打开的窗口污染后续测试的焦点/查找。
    /// </summary>
    /// <remarks>
    /// <b>深度搜索原因</b>：SettingsWindow 的 <c>Owner</c> 被设为 LoginWindow（瞬态 DI + Owner 模式），
    /// 在 UIA 树中 owned window 的 parent 是 owner 而非 desktop，<c>FindFirstChild</c> 会错过。
    /// 改用 <c>FindFirstDescendant</c> 全树搜索。
    /// <para>
    /// <b>关闭后恢复前台</b>：owned window 关闭时，Windows 按当前 z-order 把前台给「下一个窗口」，
    /// 往往是测试运行器控制台而非 LoginWindow → 主动 <c>EnsureLoginWindowForeground</c> 把焦点拉回。
    /// </para>
    /// </remarks>
    public void CloseChildWindows()
    {
        try
        {
            var cf = Automation.ConditionFactory;
            // 精确关闭 SettingsWindow（按 AutomationId）。
            // 不能按 Name/Title 判断 LoginWindow：FluentWindow 的 UIA Name 可能为空，
            // 用 Name.Contains("登录") 会误判 → 把 LoginWindow 也关了 → 后续测试全部失败。
            var settings = Automation.GetDesktop()
                .FindFirstDescendant(cf.ByAutomationId("SettingsWindow").And(cf.ByControlType(ControlType.Window)))?.AsWindow();
            if (settings is not null)
            {
                try { settings.Close(); }
                catch { /* 忽略 */ }
                // 关闭 owned window 后 Windows 不一定会把前台还给 LoginWindow，主动恢复
                EnsureLoginWindowForeground();
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>按 AutomationId 查找窗口（两段式：桌面直接子级 → 桌面全后代）。</summary>
    /// <remarks>
    /// <c>FluentWindow</c>（<c>ExtendsContentIntoTitleBar=True</c>）的 Title 和 UIA Name
    /// 在 UIA3 中都可能为空，<see cref="FindWindowByTitle"/> 不可靠。
    /// 用 <c>AutomationProperties.AutomationId</c> + 本方法是最可靠的窗口定位方式。
    /// <para>
    /// <b>两段式搜索原因</b>：当 <c>Window.Owner</c> 被设置（如 SettingsWindow.Owner = LoginWindow），
    /// owned window 在 UIA 树中的 parent 变成 owner 而非 desktop，
    /// <c>FindFirstChild</c>（仅搜桌面直接子级）会错过 owned window。
    /// 先用 <c>FindFirstChild</c>（快，命中非 owned 窗口），未命中再用 <c>FindFirstDescendant</c>（全树，命中 owned）。
    /// </para>
    /// </remarks>
    public Window? FindWindowByAutomationId(string automationId, TimeSpan? timeout = null)
    {
        var wait = timeout ?? TimeSpan.FromSeconds(8);
        var cf = Automation.ConditionFactory;
        var condition = cf.ByAutomationId(automationId).And(cf.ByControlType(ControlType.Window));
        return UiTestHelpers.WaitFor(() =>
        {
            // 1. 优先桌面直接子级（非 owned window，查找快）
            var top = Automation.GetDesktop().FindFirstChild(condition)?.AsWindow();
            if (top is not null) return top;
            // 2. fallback：桌面所有后代（owned window 的 UIA parent 是 owner 而非 desktop）
            //    注意：必须用 AsWindow() 而非 as Window——FlaUI FindFirstDescendant 返回 AutomationElement 基类，
            //    as Window 会因运行时类型不匹配返回 null，AsWindow() 才会正确创建 Window 包装器
            return Automation.GetDesktop().FindFirstDescendant(condition)?.AsWindow();
        }, wait);
    }

    private Window? FindLoginWindow()
    {
        var win = App?.GetMainWindow(Automation, TimeSpan.FromSeconds(2));
        return win is not null && win.Title.Contains("登录", StringComparison.Ordinal) ? win : null;
    }

    /// <summary>清理可能残留的 Host 进程（避免单例守卫拦截）。</summary>
    /// <remarks>
    /// <b>必须循环等待进程真正退出</b>：单例守卫用命名事件（内核对象），
    /// 进程 Kill 后内核对象有延迟释放。若新进程在内核对象释放前启动，
    /// 单例守卫会检测到「已运行实例」并拦截新进程 → LoginWindow 不出现 → 全部测试失败。
    /// </remarks>
    private static void KillOrphanHostProcesses()
    {
        foreach (var p in Process.GetProcessesByName(HostProcessName))
        {
            try
            {
                if (!p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                    // 循环等待进程真正退出（最多 10 秒），WaitForExit(3000) 不够稳定
                    for (var i = 0; i < 100 && !p.HasExited; i++)
                        Thread.Sleep(100);
                }
            }
            catch { /* 忽略 */ }
            finally { p.Dispose(); }
        }
    }

    /// <summary>
    /// 解析 Host exe 路径。从测试输出目录向上查找 repo 根（含 <c>src/</c> 的目录），
    /// 再拼 <c>src/FuturesTrader.Host/bin/Debug/net10.0-windows/FuturesTrader.Host.exe</c>。
    /// 用向上查找而非固定级数，避免目录深度变化导致路径错误。
    /// </summary>
    private static string ResolveHostExePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        if (dir is null)
            throw new DirectoryNotFoundException(
                "无法定位 repo 根（含 src/ 的目录）。测试输出目录: " + AppContext.BaseDirectory);
        return Path.Combine(dir.FullName,
            "src", "FuturesTrader.Host", "bin", "Debug", "net10.0-windows", "FuturesTrader.Host.exe");
    }
}
