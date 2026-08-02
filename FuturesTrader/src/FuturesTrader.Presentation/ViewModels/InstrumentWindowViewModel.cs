using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 单个合约窗口的视图状态（分组窗口列表中一行）。
/// 持有合约窗口标题、组号和打开状态，命令回调父 <see cref="WindowGroupBarViewModel"/>
/// 执行窗口操作。价格梯显示参数属于 Settings 的共享 Window 配置，不在列表行重复编辑。
/// </summary>
public sealed partial class InstrumentWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string InstrumentCode { get; set; } = "";

    [ObservableProperty]
    public partial int GroupId { get; set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>与合约窗口 TitleBar 使用同一格式：名称 - 代码，期权附剩余天数和到期月日。</summary>
    [ObservableProperty]
    public partial string DisplayTitle { get; private set; } = string.Empty;

    /// <summary>父 VM，命令回调用。构造时由父通过 init 设入。</summary>
    public required WindowGroupBarViewModel Parent { get; init; }

    /// <summary>从领域窗口及已缓存合约元数据加载当前行。</summary>
    internal void Hydrate(InstrumentWindow source, bool isOpen, Instrument? instrument)
    {
        InstrumentCode = source.InstrumentCode;
        GroupId = source.GroupId;
        IsOpen = isOpen;
        DisplayTitle = TradingViewModel.FormatInstrumentDisplayName(InstrumentCode, instrument, DateTime.Today);
    }

    internal void UpdateInstrument(Instrument instrument)
    {
        if (!InstrumentCode.Equals(instrument.InstrumentId, StringComparison.OrdinalIgnoreCase)) return;
        DisplayTitle = TradingViewModel.FormatInstrumentDisplayName(InstrumentCode, instrument, DateTime.Today);
    }

    [RelayCommand]
    private void Focus() => Parent.FocusWindow(InstrumentCode);

    [RelayCommand]
    private void Close() => Parent.CloseWindow(InstrumentCode);

    [RelayCommand]
    private void Unassign() => Parent.UnassignWindow(InstrumentCode);

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    private void MoveUp() => Parent.MoveWindow(InstrumentCode, -1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    private void MoveDown() => Parent.MoveWindow(InstrumentCode, +1);

    /// <summary>是否可在当前组内上移（不在首位时 true）。</summary>
    public bool CanMoveUp() => Parent.CanMoveWindowUp(InstrumentCode);

    /// <summary>是否可在当前组内下移（不在末位时 true）。</summary>
    public bool CanMoveDown() => Parent.CanMoveWindowDown(InstrumentCode);
}
