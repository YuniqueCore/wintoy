using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 单个分组（1-20 号之一）的视图状态：显示名、是否重命名中、组内窗口列表。
/// 命令：OpenGroup（点击打开整组 + 选中该组）、BeginRename/CommitRename/CancelRename（内联重命名）。
/// 重命名用 IsRenaming 切 TextBlock/TextBox，零 Converter（DataTrigger 控制 Visibility）。
/// </summary>
public sealed partial class WindowGroupViewModel : ObservableObject
{
    [ObservableProperty]
    public partial int Id { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    /// <summary>是否处于重命名编辑态（true 时显示 TextBox 替代 TextBlock）。</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>重命名输入框文本，BeginRename 时初始化为当前 Name。</summary>
    [ObservableProperty]
    public partial string RenameText { get; set; } = "";

    /// <summary>组内窗口列表（由父 Hydrate 同步）。</summary>
    public ObservableCollection<InstrumentWindowViewModel> Windows { get; } = [];

    public required WindowGroupBarViewModel Parent { get; init; }

    [RelayCommand]
    private async Task OpenGroupAsync() => await Parent.OpenGroupAsync(Id);

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!string.IsNullOrWhiteSpace(RenameText))
            Parent.RenameGroup(Id, RenameText.Trim());
        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;
}
