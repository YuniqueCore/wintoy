using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 合约窗口宿主抽象：统一管理合约窗口的打开/聚焦/关闭。
/// 解耦窗口分组模块与具体窗口实现（旧软件 TYYWin / 未来 TradingView）。
/// 实现须在 UI 线程调度窗口操作（WPF Window.Show() 要求 UI 线程），
/// 故 Open/Focus/Close 内部应通过 Dispatcher.Invoke 兜底，支持 MCP HTTP 线程触发。
/// </summary>
public interface IWindowHost
{
    /// <summary>指定合约窗口是否已打开。</summary>
    bool IsOpen(string instrumentCode);

    /// <summary>打开合约窗口（已打开则聚焦，不重复创建）。</summary>
    void Open(InstrumentWindow window);

    /// <summary>聚焦已打开的合约窗口。</summary>
    void Focus(string instrumentCode);

    /// <summary>关闭已打开的合约窗口。</summary>
    void Close(string instrumentCode);
}
