using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 单个合约窗口的视图状态（分组窗口列表中一行）。
/// 持有合约码/组号/打开状态，命令回调父 <see cref="WindowGroupBarViewModel"/> 执行聚焦/关闭/解绑。
/// </summary>
public sealed partial class InstrumentWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string InstrumentCode { get; set; } = "";

    [ObservableProperty]
    public partial int GroupId { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>父 VM，命令回调用。构造时由父通过 init 设入。</summary>
    public required WindowGroupBarViewModel Parent { get; init; }

    [RelayCommand]
    private void Focus() => Parent.FocusWindow(InstrumentCode);

    [RelayCommand]
    private void Close() => Parent.CloseWindow(InstrumentCode);

    [RelayCommand]
    private void Unassign() => Parent.UnassignWindow(InstrumentCode);
}
