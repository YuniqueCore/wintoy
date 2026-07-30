using System.Runtime.InteropServices;

namespace FuturesTrader.UiAutomationTests.Fixtures;

/// <summary>
/// Win32 前台窗口强制辅助：用 <c>AttachThreadInput</c> 绕过 Windows 反偷焦机制，
/// 把指定窗口强制设为前台窗口。
/// <para>
/// <b>为什么需要</b>：FlaUI 的 <c>Window.FocusNative</c> 仅调用 <c>SetForegroundWindow</c>，
/// 而 Windows 有反偷焦限制——调用线程必须是当前前台线程，或刚收到用户输入，
/// 否则 <c>SetForegroundWindow</c> 静默失败（窗口只在任务栏闪烁，不会真正到前台）。
/// 测试运行器控制台在每个 <c>[Fact]</c> 启动时抢焦，LoginWindow 失去前台 →
/// 后续 <c>Click()</c>/<c>SetFocus()</c> 落空 → 焦点落到桌面/控制台（"回到桌面"现象）。
/// </para>
/// <para>
/// <b>原理</b>：<c>AttachThreadInput</c> 把当前线程的输入队列附加到目标窗口所在线程，
/// 让 Windows 认为当前线程「就是」前台线程，从而允许 <c>SetForegroundWindow</c>。
/// 这是 Win32 强制前台的标准手法，比单纯 <c>SetForegroundWindow</c> 可靠得多。
/// </para>
/// </summary>
public static class Win32ForegroundHelper
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    /// <summary>
    /// 强制把指定窗口设为前台窗口，绕过 Windows 反偷焦限制。
    /// </summary>
    /// <param name="hWnd">目标窗口 HWND。为 <see cref="IntPtr.Zero"/> 时直接返回。</param>
    /// <remarks>
    /// 步骤：
    /// 1. 取当前前台线程 ID + 目标线程 ID + 当前线程 ID
    /// 2. <c>AttachThreadInput</c> 把当前线程附加到前台线程和目标线程（共享输入状态）
    /// 3. 若目标窗口最小化（<c>IsIconic</c>）则 <c>ShowWindow(SW_RESTORE)</c> 恢复
    /// 4. <c>BringWindowToTop</c> + <c>SetForegroundWindow</c>
    /// 5. <c>finally</c> 反向 <c>AttachThreadInput</c> 解除附加（必须解除，否则输入状态泄漏）
    /// </remarks>
    public static void ForceForeground(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return;

        var currentThreadId = GetCurrentThreadId();
        var foreground = GetForegroundWindow();
        var foregroundThreadId = foreground == IntPtr.Zero
            ? currentThreadId
            : GetWindowThreadProcessId(foreground, out _);
        var targetThreadId = GetWindowThreadProcessId(hWnd, out _);
        // 目标窗口无效（HWND 已销毁）→ 直接返回，避免对 0 线程 AttachThreadInput
        if (targetThreadId == 0) return;

        var attachFg = foregroundThreadId != currentThreadId && foregroundThreadId != 0;
        var attachTarget = targetThreadId != currentThreadId;
        try
        {
            if (attachFg) AttachThreadInput(currentThreadId, foregroundThreadId, true);
            if (attachTarget) AttachThreadInput(currentThreadId, targetThreadId, true);

            if (IsIconic(hWnd)) ShowWindow(hWnd, SwRestore);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
        }
        finally
        {
            // 必须解除附加，否则线程输入状态泄漏，影响后续窗口前台切换
            if (attachTarget) AttachThreadInput(currentThreadId, targetThreadId, false);
            if (attachFg) AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }
}
