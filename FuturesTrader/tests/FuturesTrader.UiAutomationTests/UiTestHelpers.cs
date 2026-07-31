using FlaUI.Core.AutomationElements;
using FuturesTrader.UiAutomationTests.Fixtures;

namespace FuturesTrader.UiAutomationTests;

/// <summary>
/// UI 自动化测试辅助：统一的等待与元素查找工具。
/// FlaUI 4.0 的 <c>Wait.For</c> 不存在（只有 <c>Wait.While/Until</c> 返回 bool），
/// 这里提供返回值的轮询等待，用于异步加载的 UI 元素（如 DataGrid 数据、窗口出现）。
/// </summary>
public static class UiTestHelpers
{
    /// <summary>轮询 getter 直到返回非 null 值或超时。用于等待异步出现的 UI 元素。</summary>
    public static T? WaitFor<T>(Func<T?> getter, TimeSpan timeout, int intervalMs = 200) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = getter();
            if (result is not null) return result;
            Thread.Sleep(intervalMs);
        }
        return null;
    }

    /// <summary>轮询 condition 直到返回 true 或超时。用于等待布尔状态（如按钮启用）。</summary>
    public static bool WaitTrue(Func<bool> condition, TimeSpan timeout, int intervalMs = 200)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(intervalMs);
        }
        return false;
    }

    /// <summary>轮询 getter 直到返回非 null 值或超时（值类型版本）。</summary>
    public static T? WaitForValue<T>(Func<T?> getter, TimeSpan timeout, int intervalMs = 200) where T : struct
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = getter();
            if (result.HasValue) return result;
            Thread.Sleep(intervalMs);
        }
        return null;
    }

    /// <summary>按名称查找按钮并点击。返回按钮元素（供断言）。</summary>
    public static Button FindButton(Window window, string name) =>
        window.FindFirstDescendant(window.Automation.ConditionFactory.ByName(name))?.AsButton()
            ?? throw new Xunit.Sdk.XunitException($"未找到按钮 '{name}'");

    /// <summary>
    /// 从当前 UIA 树重新定位登录窗口中的“设置”按钮。
    /// SettingsWindow 开闭或主题切换后，缓存的 <see cref="Window"/> 包装对象可能不再反映当前子树；
    /// 用稳定的 AutomationId 和即时刷新避免把 UIA 缓存抖动误判为功能缺失。
    /// </summary>
    public static Button? FindOpenSettingsButton(HostAppFixture fixture) =>
        fixture.RefreshLoginWindow()?.FindFirstDescendant(
            fixture.Automation.ConditionFactory.ByAutomationId("OpenSettingsButton"))?.AsButton();

    /// <summary>
    /// Click 按钮 + 等待目标窗口出现，失败则重试。先检查窗口是否已存在避免重复点击堆叠多窗口。
    /// </summary>
    /// <param name="fixture">Host fixture，提供 <see cref="HostAppFixture.FindWindowByAutomationId"/> 和 <see cref="HostAppFixture.EnsureLoginWindowForeground"/>。</param>
    /// <param name="button">要点击的按钮元素。</param>
    /// <param name="windowAutomationId">点击后应出现的窗口的 AutomationId。</param>
    /// <param name="maxAttempts">最大重试次数（默认 3）。</param>
    /// <returns>找到的窗口；全部重试失败返回 null。</returns>
    /// <remarks>
    /// <b>为什么先检查再点击</b>：SettingsWindow 在 DI 中是瞬态，每次 Click「设置」按钮都会新开一个实例。
    /// 若上次重试已成功开窗但 <see cref="HostAppFixture.FindWindowByAutomationId"/> 因 UIA 树延迟未命中，
    /// 直接再点会堆叠第二个 SettingsWindow。先检查可短路返回已存在的窗口。
    /// </remarks>
    public static Window? ClickUntilWindowAppears(
        HostAppFixture fixture,
        Button button,
        string windowAutomationId,
        int maxAttempts = 3)
    {
        // 0. 先检查是否已存在（前置测试残留或上次重试已开）——避免重复点击堆叠多窗口
        var existing = fixture.FindWindowByAutomationId(windowAutomationId, TimeSpan.FromSeconds(1));
        if (existing is not null) return existing;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            // 每次点击前确保前台：WPF UI Button 的 Click 在窗口非前台时可能不触发 Command
            fixture.EnsureLoginWindowForeground();
            button.Click();
            var window = fixture.FindWindowByAutomationId(windowAutomationId, TimeSpan.FromSeconds(3));
            if (window is not null) return window;
            // 重试前短暂等待，让 UIA 树有时间刷新
            System.Threading.Thread.Sleep(200);
        }
        return null;
    }
}
