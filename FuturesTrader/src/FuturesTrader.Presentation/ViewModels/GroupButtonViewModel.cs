using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 浮动栏分组按钮 ViewModel（1-20 号）：单击打开该组全部合约窗口。
/// <see cref="Name"/> 支持重命名（双击编辑），<see cref="WindowCount"/> 展示组内窗口数。
/// </summary>
public sealed partial class GroupButtonViewModel : ObservableObject
{
    /// <summary>分组号 1-20。</summary>
    public int Id { get; init; }

    /// <summary>分组名称（默认「组 N」，可重命名）。</summary>
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>组内窗口数；0 仍可点击，以便选为空组后通过搜索添加合约。</summary>
    [ObservableProperty] private int _windowCount;

    /// <summary>是否选中（单选高亮）。</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>父 VM 引用（点击时回调 OpenGroupAsync）。</summary>
    public required FloatingMainViewModel Parent { get; init; }

    /// <summary>点击分组按钮：通知父 VM 打开该组。</summary>
    [RelayCommand]
    private void Open()
    {
        Parent.OpenGroupFromButton(Id);
    }
}
