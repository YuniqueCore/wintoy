using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Application;
using FuturesTrader.Domain.WindowGroups;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 窗口分组管理段的 ViewModel：编排 20 个分组按钮 + 当前组窗口列表 + 合约绑定输入。
/// 持有内存中的 <see cref="WindowLayout"/>（_loaded），所有变更（绑定/解绑/重命名）先改 _loaded，
/// 由用户点保存持久化（Load/Save 模式，与配置编辑器一致）。OpenGroup 用 _loaded 知道组内窗口。
/// 状态机 <see cref="WindowGroupEditorState"/> 驱动 UI 反馈。
/// </summary>
public sealed partial class WindowGroupBarViewModel : ObservableObject
{
    private readonly WindowGroupService _service;
    private readonly ILogger<WindowGroupBarViewModel> _logger;
    private WindowLayout? _loaded;

    public WindowGroupBarViewModel(WindowGroupService service, ILogger<WindowGroupBarViewModel> logger)
    {
        _service = service;
        _logger = logger;
        Groups = new ObservableCollection<WindowGroupViewModel>(
            Enumerable.Range(1, 20).Select(i => new WindowGroupViewModel
            {
                Id = i,
                Name = $"组 {i}",
                Parent = this
            }));
        SelectedGroup = Groups[0];
    }

    /// <summary>20 个分组按钮 VM（1-20 号）。</summary>
    public ObservableCollection<WindowGroupViewModel> Groups { get; }

    /// <summary>当前选中的分组（窗口列表展示其窗口，绑定输入指向它）。</summary>
    [ObservableProperty]
    public partial WindowGroupViewModel? SelectedGroup { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial WindowGroupEditorState State { get; private set; } = new WindowGroupEditorState.Idle();

    /// <summary>绑定输入框：新合约码。</summary>
    [ObservableProperty]
    public partial string NewInstrumentCode { get; set; } = "";

    [ObservableProperty]
    public partial DateTime? LastSavedAt { get; set; }

    /// <summary>首次切换到本段时自动加载（由 SettingsViewModel 触发），已加载则跳过。</summary>
    public void EnsureLoaded()
    {
        if (_loaded is null && State is WindowGroupEditorState.Idle)
            _ = LoadAsync();
    }

    [RelayCommand(CanExecute = nameof(CanLoadOrSave))]
    private async Task LoadAsync()
    {
        State = new WindowGroupEditorState.Loading();
        try
        {
            _loaded = await Task.Run(() => _service.Load());
            HydrateGroups(_loaded);
            State = new WindowGroupEditorState.Loaded();
            _logger.LogInformation("窗口分组已加载: {Count} 个窗口", _loaded.Windows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载窗口分组失败");
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (_loaded is null) return;
        State = new WindowGroupEditorState.Saving();
        try
        {
            var toSave = _loaded;
            await Task.Run(() => _service.Save(toSave));
            LastSavedAt = DateTime.Now;
            State = new WindowGroupEditorState.Loaded();
            _logger.LogInformation("窗口分组已保存");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存窗口分组失败");
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    /// <summary>将输入框合约码绑定到当前选中分组（改 _loaded，需保存持久化）。</summary>
    [RelayCommand]
    private async Task AssignWindowAsync()
    {
        if (_loaded is null)
        {
            State = new WindowGroupEditorState.Error("请先加载窗口分组");
            return;
        }
        if (SelectedGroup is null) return;
        var code = NewInstrumentCode.Trim();
        if (string.IsNullOrEmpty(code)) return;
        try
        {
            _loaded = _service.AssignWindowToGroup(_loaded, code, SelectedGroup.Id);
            NewInstrumentCode = "";
            HydrateGroups(_loaded);
            State = new WindowGroupEditorState.Loaded();
        }
        catch (Exception ex)
        {
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    /// <summary>点击分组按钮：选中该组 + 打开整组窗口（未加载则先加载）。</summary>
    public async Task OpenGroupAsync(int groupId)
    {
        try
        {
            if (_loaded is null)
            {
                State = new WindowGroupEditorState.Loading();
                _loaded = await Task.Run(() => _service.Load());
            }
            SelectedGroup = Groups.FirstOrDefault(g => g.Id == groupId);
            _service.OpenGroup(_loaded, groupId);
            HydrateGroups(_loaded);
            State = new WindowGroupEditorState.Loaded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开分组 {GroupId} 失败", groupId);
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    /// <summary>重命名分组（改 _loaded，需保存持久化）。</summary>
    public void RenameGroup(int groupId, string newName)
    {
        if (_loaded is null) return;
        try
        {
            _loaded = _service.RenameGroup(_loaded, groupId, newName);
            HydrateGroups(_loaded);
        }
        catch (Exception ex)
        {
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    /// <summary>解绑合约窗口（从 _loaded 移除，需保存持久化）。</summary>
    public void UnassignWindow(string instrumentCode)
    {
        if (_loaded is null) return;
        try
        {
            _loaded = _service.UnassignWindow(_loaded, instrumentCode);
            HydrateGroups(_loaded);
        }
        catch (Exception ex)
        {
            State = new WindowGroupEditorState.Error(ex.Message);
        }
    }

    public void FocusWindow(string instrumentCode)
    {
        try { _service.FocusWindow(instrumentCode); }
        catch (Exception ex) { State = new WindowGroupEditorState.Error(ex.Message); }
    }

    public void CloseWindow(string instrumentCode)
    {
        try
        {
            _service.CloseWindow(instrumentCode);
            if (_loaded is not null) HydrateGroups(_loaded);
        }
        catch (Exception ex) { State = new WindowGroupEditorState.Error(ex.Message); }
    }

    /// <summary>把 _loaded 的组名 + 各组窗口同步到 VM（await 后在 UI 线程操作集合）。</summary>
    private void HydrateGroups(WindowLayout layout)
    {
        foreach (var vm in Groups)
        {
            var group = layout.Groups.FirstOrDefault(g => g.Id == vm.Id);
            vm.Name = group?.Name ?? $"组 {vm.Id}";
            vm.Windows.Clear();
            foreach (var w in layout.Windows.Where(w => w.GroupId == vm.Id))
            {
                vm.Windows.Add(new InstrumentWindowViewModel
                {
                    InstrumentCode = w.InstrumentCode,
                    GroupId = w.GroupId,
                    IsOpen = _service.IsWindowOpen(w.InstrumentCode),
                    Parent = this
                });
            }
        }
    }

    private bool CanLoadOrSave() =>
        State is WindowGroupEditorState.Idle or WindowGroupEditorState.Loaded or WindowGroupEditorState.Error;

    private bool CanSave() => State is WindowGroupEditorState.Loaded;
}
