using System.Windows;
using System.Windows.Controls;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Presentation.WindowHosting;

/// <summary>
/// <see cref="IWindowHost"/> 的桩实现：开轻量占位窗口（标题 + 文本），用于 TYYWin 未接入前的端到端验证。
/// 用 Dictionary 跟踪已开窗口，Open 已开则 Activate，Closed 事件移除字典项。
/// Open/Focus/Close 内部用 <see cref="Application.Dispatcher"/>.Invoke 兜底：VM 触发时是 no-op 同步，
/// MCP HTTP 线程触发时 marshalled 到 UI 线程，避免 Window.Show() 跨线程异常。
/// TYYWin 实现后替换为本接口的真实实现即可，分组模块无需改动。
/// </summary>
public sealed class StubWindowHost : IWindowHost
{
    private readonly Dictionary<string, Window> _open = new();

    public bool IsOpen(string instrumentCode) => _open.ContainsKey(instrumentCode);

    public void Open(InstrumentWindow window) => OnUi(() =>
    {
        if (_open.TryGetValue(window.InstrumentCode, out var existing))
        {
            existing.Activate();
            return;
        }
        var stub = new Window
        {
            Title = $"{window.InstrumentCode} · 组 {window.GroupId}",
            Width = Math.Max(window.Width, 220),
            Height = Math.Max(window.Height, 160),
            Left = window.Left,
            Top = window.Top,
            Content = new TextBlock
            {
                Text = $"合约: {window.InstrumentCode}\n组号: {window.GroupId}\n(StubWindowHost 占位窗口)",
                Margin = new Thickness(16),
                FontSize = 14
            }
        };
        stub.Closed += (_, _) => _open.Remove(window.InstrumentCode);
        _open[window.InstrumentCode] = stub;
        stub.Show();
    });

    public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId) => OnUi(() =>
    {
        foreach (var w in windows) Open(w);
    });

    public IReadOnlyList<string> GetOpenWindowsInGroup(int groupId) =>
        _open.Keys.ToList();

    public void HideGroup(int groupId) { }

    public void CloseGroup(int groupId) => OnUi(() =>
    {
        foreach (var key in _open.Keys.ToList())
            _open[key].Close();
    });

    public void Focus(string instrumentCode) => OnUi(() =>
    {
        if (_open.TryGetValue(instrumentCode, out var w))
            w.Activate();
    });

    public void Close(string instrumentCode) => OnUi(() =>
    {
        if (_open.TryGetValue(instrumentCode, out var w))
            w.Close();
    });

    /// <summary>在 UI 线程执行；无 WPF 应用上下文时（如单元测试）直接内联执行。
    /// 用完全限定 System.Windows.Application 避免与 FuturesTrader.Application 命名空间冲突。</summary>
    private static void OnUi(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            action();
            return;
        }
        app.Dispatcher.Invoke(action);
    }
}
