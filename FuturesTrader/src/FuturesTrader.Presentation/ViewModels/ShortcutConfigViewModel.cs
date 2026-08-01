using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.Services;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>快捷键录制草稿：负责录制状态、冲突校验和默认值恢复，不执行交易动作。</summary>
public sealed partial class ShortcutConfigViewModel : ObservableObject
{
    private static readonly ShortcutConfig Defaults = new();

    public ShortcutConfigViewModel(ShortcutConfig configuration)
    {
        Bindings = new ObservableCollection<ShortcutBindingViewModel>(
        [
            Create(KeyboardShortcutAction.SelectiveCancelAll, "选择性全撤", "仅撤销符合旧版 Space 条件的可见窗口活动单", configuration.SelectiveCancelAll),
            Create(KeyboardShortcutAction.ForceCancelAll, "强制全撤", "向全部已创建合约窗口提交撤单请求", configuration.ForceCancelAll),
            Create(KeyboardShortcutAction.RecenterAsk, "定位卖一", "把所有可见价格梯定位到最优卖价", configuration.RecenterAsk),
            Create(KeyboardShortcutAction.RecenterBid, "定位买一", "把所有可见价格梯定位到最优买价", configuration.RecenterBid),
            Create(KeyboardShortcutAction.ToggleOnlyOpen, "切换只开仓", "切换当前窗口 OnlyOpen 开仓模式", configuration.ToggleOnlyOpen),
            Create(KeyboardShortcutAction.MoveSelectionUp, "上移一格", "当前窗口键盘选中价位上移", configuration.MoveSelectionUp),
            Create(KeyboardShortcutAction.MoveSelectionDown, "下移一格", "当前窗口键盘选中价位下移", configuration.MoveSelectionDown)
        ]);
    }

    public ObservableCollection<ShortcutBindingViewModel> Bindings { get; }

    [ObservableProperty]
    public partial ShortcutBindingViewModel? RecordingBinding { get; private set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; private set; } = string.Empty;

    [RelayCommand]
    private void BeginRecording(ShortcutBindingViewModel binding)
    {
        if (RecordingBinding is not null) RecordingBinding.IsRecording = false;
        RecordingBinding = binding;
        binding.IsRecording = true;
        ValidationMessage = $"正在录制“{binding.ActionName}”：请按新快捷键，Esc 取消";
    }

    public bool TryAssign(Key key, ModifierKeys modifiers)
    {
        if (RecordingBinding is null) return false;
        if (key == Key.Escape)
        {
            StopRecording();
            ValidationMessage = "已取消快捷键录制";
            return true;
        }
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return false;

        var gesture = new ShortcutGesture(key, modifiers);
        var conflict = Bindings.FirstOrDefault(binding =>
            binding != RecordingBinding && Parse(binding.GestureText) is { } parsed && parsed == gesture);
        if (conflict is not null)
        {
            ValidationMessage = $"快捷键 {ShortcutGestureParser.Format(key, modifiers)} 已分配给“{conflict.ActionName}”";
            return false;
        }

        RecordingBinding.GestureText = ShortcutGestureParser.Format(key, modifiers);
        StopRecording();
        ValidationMessage = "快捷键已录制；保存配置后立即生效";
        return true;
    }

    [RelayCommand]
    private void ResetOne(ShortcutBindingViewModel binding)
    {
        var defaultText = GetGesture(Defaults, binding.Action);
        var defaultGesture = Parse(defaultText)!.Value;
        var conflict = Bindings.FirstOrDefault(candidate =>
            candidate != binding && Parse(candidate.GestureText) is { } parsed && parsed == defaultGesture);
        if (conflict is not null)
        {
            ValidationMessage = $"默认快捷键 {defaultText} 已分配给“{conflict.ActionName}”；可恢复全部默认后再调整";
            return;
        }
        binding.GestureText = defaultText;
        ValidationMessage = $"“{binding.ActionName}”已恢复默认；保存配置后生效";
    }

    [RelayCommand]
    private void ResetAll()
    {
        foreach (var binding in Bindings)
            binding.GestureText = GetGesture(Defaults, binding.Action);
        StopRecording();
        ValidationMessage = "全部快捷键已恢复默认；保存配置后生效";
    }

    public void Hydrate(ShortcutConfig configuration)
    {
        foreach (var binding in Bindings)
            binding.GestureText = GetGesture(configuration, binding.Action);
        StopRecording();
        ValidationMessage = string.Empty;
    }

    public ShortcutConfig ToConfig() => new()
    {
        SelectiveCancelAll = Find(KeyboardShortcutAction.SelectiveCancelAll).GestureText,
        ForceCancelAll = Find(KeyboardShortcutAction.ForceCancelAll).GestureText,
        RecenterAsk = Find(KeyboardShortcutAction.RecenterAsk).GestureText,
        RecenterBid = Find(KeyboardShortcutAction.RecenterBid).GestureText,
        ToggleOnlyOpen = Find(KeyboardShortcutAction.ToggleOnlyOpen).GestureText,
        MoveSelectionUp = Find(KeyboardShortcutAction.MoveSelectionUp).GestureText,
        MoveSelectionDown = Find(KeyboardShortcutAction.MoveSelectionDown).GestureText
    };

    private void StopRecording()
    {
        if (RecordingBinding is not null) RecordingBinding.IsRecording = false;
        RecordingBinding = null;
    }

    private ShortcutBindingViewModel Find(KeyboardShortcutAction action) =>
        Bindings.Single(binding => binding.Action == action);

    private static ShortcutBindingViewModel Create(
        KeyboardShortcutAction action,
        string name,
        string description,
        string gesture) => new(action, name, description, gesture);

    private static ShortcutGesture? Parse(string text)
    {
        return ShortcutGestureParser.TryParse(text, out var gesture) ? gesture : null;
    }

    private static string GetGesture(ShortcutConfig config, KeyboardShortcutAction action) => action switch
    {
        KeyboardShortcutAction.SelectiveCancelAll => config.SelectiveCancelAll,
        KeyboardShortcutAction.ForceCancelAll => config.ForceCancelAll,
        KeyboardShortcutAction.RecenterAsk => config.RecenterAsk,
        KeyboardShortcutAction.RecenterBid => config.RecenterBid,
        KeyboardShortcutAction.ToggleOnlyOpen => config.ToggleOnlyOpen,
        KeyboardShortcutAction.MoveSelectionUp => config.MoveSelectionUp,
        KeyboardShortcutAction.MoveSelectionDown => config.MoveSelectionDown,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
    };
}

public sealed class ShortcutBindingViewModel : ObservableObject
{
    private string _gestureText;
    private bool _isRecording;

    public ShortcutBindingViewModel(
        KeyboardShortcutAction action,
        string actionName,
        string description,
        string gestureText)
    {
        Action = action;
        ActionName = actionName;
        Description = description;
        _gestureText = gestureText;
    }

    public KeyboardShortcutAction Action { get; }
    public string ActionName { get; }
    public string Description { get; }
    public string GestureText { get => _gestureText; set => SetProperty(ref _gestureText, value); }
    public bool IsRecording { get => _isRecording; set => SetProperty(ref _isRecording, value); }
}
