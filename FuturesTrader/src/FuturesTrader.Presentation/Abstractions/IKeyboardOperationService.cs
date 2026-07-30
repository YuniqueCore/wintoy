using System.Windows.Input;

namespace FuturesTrader.Presentation.Abstractions;

/// <summary>
/// 集中化键盘操作服务抽象：统一注册/派发快捷键，避免散落在各 View 的 KeyBinding。
/// 当前接 PriceListControl 上下导航（Up/Down）+ 选中价位变更事件；
/// M3 扩展时在此注册买卖/撤单热键（F1 买开/F2 卖开/F3 撤单 等）。
/// 实现为全局单例，跨窗口共享一份按键映射表。
/// 放在 Presentation 层而非 Application，因 KeyGesture/KeyEventArgs 是 WPF 类型，
/// Application 层目标为 net10.0（非 windows），不应耦合 WPF。
/// </summary>
public interface IKeyboardOperationService
{
    /// <summary>当前选中的价位索引（PriceListControl 上下导航用，-1 表示未选中）。</summary>
    int SelectedPriceIndex { get; }

    /// <summary>选中价位变更事件（Up/Down 导航时触发，PriceListControl 订阅以滚动到选中行）。</summary>
    event EventHandler<int>? SelectedPriceIndexChanged;

    /// <summary>注册快捷键 → 回调（同一手势重复注册后者覆盖前者）。</summary>
    void Register(KeyGesture gesture, Action action, string? description = null);

    /// <summary>注销快捷键。</summary>
    void Unregister(KeyGesture gesture);

    /// <summary>处理 WPF KeyEventArgs：命中已注册手势则执行回调并返回 true。</summary>
    bool Handle(KeyEventArgs e);

    /// <summary>移动选中价位（offset=±1 为上下移动一格；越界时夹紧到 [0, maxIndex]）。</summary>
    void MoveSelection(int offset, int maxIndex);
}
