using System.IO;
using System.Windows.Input;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Presentation.Abstractions;
using Microsoft.Extensions.Options;

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
    private IReadOnlyDictionary<KeyboardShortcutAction, ShortcutGesture> _configuredBindings =
        new Dictionary<KeyboardShortcutAction, ShortcutGesture>();

    public KeyboardOperationService()
    {
        if (!TryApplyConfiguration(new ShortcutConfig(), out var error))
            throw new InvalidOperationException(error);
    }

    public KeyboardOperationService(IConfigRepository repository, IOptions<ConfigFileOptions> options)
        : this()
    {
        try
        {
            var persisted = repository.Load(options.Value.Path).Shortcuts;
            _ = TryApplyConfiguration(persisted, out _);
        }
        catch (FileNotFoundException)
        {
            // 首次启动尚无 config.ini 时继续使用领域默认键位。
        }
    }

    /// <inheritdoc />
    public ShortcutConfig CurrentConfiguration { get; private set; } = new();

    /// <inheritdoc />
    public bool TryApplyConfiguration(ShortcutConfig configuration, out string error)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var parsed = new Dictionary<KeyboardShortcutAction, ShortcutGesture>();
        var owners = new Dictionary<ShortcutGesture, KeyboardShortcutAction>();

        foreach (var (action, gestureText) in Enumerate(configuration))
        {
            if (!ShortcutGestureParser.TryParse(gestureText, out var gesture))
            {
                error = $"{GetActionName(action)} 的快捷键“{gestureText}”无效";
                return false;
            }
            if (owners.TryGetValue(gesture, out var existing))
            {
                error = $"快捷键 {gestureText} 已分配给“{GetActionName(existing)}”";
                return false;
            }
            parsed[action] = gesture;
            owners[gesture] = action;
        }

        _configuredBindings = parsed;
        CurrentConfiguration = configuration;
        error = string.Empty;
        return true;
    }

    /// <inheritdoc />
    public bool Matches(KeyboardShortcutAction action, KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return _configuredBindings.TryGetValue(action, out var gesture)
            && gesture.Matches(e);
    }

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

    internal static string GetActionName(KeyboardShortcutAction action) => action switch
    {
        KeyboardShortcutAction.SelectiveCancelAll => "选择性全撤",
        KeyboardShortcutAction.ForceCancelAll => "强制全撤",
        KeyboardShortcutAction.RecenterAsk => "定位卖一",
        KeyboardShortcutAction.RecenterBid => "定位买一",
        KeyboardShortcutAction.ToggleOnlyOpen => "切换只开仓",
        KeyboardShortcutAction.MoveSelectionUp => "上移一格",
        KeyboardShortcutAction.MoveSelectionDown => "下移一格",
        _ => action.ToString()
    };

    private static IEnumerable<(KeyboardShortcutAction Action, string Gesture)> Enumerate(ShortcutConfig config)
    {
        yield return (KeyboardShortcutAction.SelectiveCancelAll, config.SelectiveCancelAll);
        yield return (KeyboardShortcutAction.ForceCancelAll, config.ForceCancelAll);
        yield return (KeyboardShortcutAction.RecenterAsk, config.RecenterAsk);
        yield return (KeyboardShortcutAction.RecenterBid, config.RecenterBid);
        yield return (KeyboardShortcutAction.ToggleOnlyOpen, config.ToggleOnlyOpen);
        yield return (KeyboardShortcutAction.MoveSelectionUp, config.MoveSelectionUp);
        yield return (KeyboardShortcutAction.MoveSelectionDown, config.MoveSelectionDown);
    }
}

internal readonly record struct ShortcutGesture(Key Key, ModifierKeys Modifiers)
{
    internal bool Matches(KeyEventArgs e)
    {
        var eventKey = e.Key == Key.System ? e.SystemKey : e.Key;
        return eventKey == Key && Keyboard.Modifiers == Modifiers;
    }
}

internal static class ShortcutGestureParser
{
    internal static bool TryParse(string? text, out ShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            var parsed = parts[index].ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModifierKeys.Control,
                "ALT" => ModifierKeys.Alt,
                "SHIFT" => ModifierKeys.Shift,
                "WIN" or "WINDOWS" => ModifierKeys.Windows,
                _ => ModifierKeys.None
            };
            if (parsed == ModifierKeys.None || modifiers.HasFlag(parsed)) return false;
            modifiers |= parsed;
        }

        var keyText = parts[^1];
        if (!Enum.TryParse<Key>(keyText, ignoreCase: true, out var key) || !IsUsableKey(key)) return false;
        gesture = new ShortcutGesture(key, modifiers);
        return true;
    }

    internal static string Format(Key key, ModifierKeys modifiers)
    {
        var parts = new List<string>(5);
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    private static bool IsUsableKey(Key key) => key is not (
        Key.None or Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin);
}
