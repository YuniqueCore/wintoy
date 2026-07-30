using System.Windows.Input;
using FuturesTrader.Presentation.Abstractions;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// <see cref="IKeyboardOperationService"/> 实现：全局单例，集中化快捷键注册/派发。
/// 用 <see cref="Dictionary{TKey,TValue}"/> 维护 KeyGesture→Action 映射（KeyGesture 已实现相等性比较）。
/// <see cref="MoveSelection"/> 维护当前选中价位索引，通过 <see cref="SelectedPriceIndexChanged"/> 通知 PriceListControl 滚动。
/// 线程安全：UI 线程单线程访问（KeyBinding 在 UI 线程触发），无需锁。
/// </summary>
public sealed class KeyboardOperationService : IKeyboardOperationService
{
    private readonly Dictionary<KeyGesture, Action> _bindings = new();
    private readonly Dictionary<KeyGesture, string> _descriptions = new();

    /// <inheritdoc />
    public int SelectedPriceIndex { get; private set; } = -1;

    /// <inheritdoc />
    public event EventHandler<int>? SelectedPriceIndexChanged;

    /// <inheritdoc />
    public void Register(KeyGesture gesture, Action action, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(action);
        _bindings[gesture] = action;
        if (description is not null)
            _descriptions[gesture] = description;
        else
            _descriptions.Remove(gesture);
    }

    /// <inheritdoc />
    public void Unregister(KeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        _bindings.Remove(gesture);
        _descriptions.Remove(gesture);
    }

    /// <inheritdoc />
    public bool Handle(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        // KeyGesture.Matches 比对按键 + 修饰键（从全局 Keyboard 状态读取）
        var device = Keyboard.PrimaryDevice;
        foreach (var (gesture, action) in _bindings)
        {
            if (gesture.Matches(device, e))
            {
                action.Invoke();
                e.Handled = true;
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public void MoveSelection(int offset, int maxIndex)
    {
        if (maxIndex < 0) return;
        // 未选中（-1）时 +1 落到 0（第一行）；越界夹紧到 [0, maxIndex]
        var newIndex = Math.Clamp(SelectedPriceIndex + offset, 0, maxIndex);
        if (newIndex == SelectedPriceIndex) return;
        SelectedPriceIndex = newIndex;
        SelectedPriceIndexChanged?.Invoke(this, newIndex);
    }

    /// <summary>重置选中状态（窗口关闭/切换合约时调用）。</summary>
    public void ResetSelection()
    {
        if (SelectedPriceIndex == -1) return;
        SelectedPriceIndex = -1;
        SelectedPriceIndexChanged?.Invoke(this, -1);
    }
}
