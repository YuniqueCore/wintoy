using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Application;

/// <summary>
/// 窗口分组编排服务（无状态）：在 <see cref="WindowLayout"/> 上做分组绑定/解绑/重命名/开组等
/// 纯领域变换，并转发 <see cref="IWindowHost"/> 的窗口操作。所有入参在此做边界校验
/// （组号 1-20、合约码非空），越界抛 <see cref="ArgumentOutOfRangeException"/>/<see cref="ArgumentException"/>。
/// 仓库读写由调用方决定时机（Load/Save 转发，不自动持久化），保持与配置编辑器一致的 Load/Save 模式。
/// </summary>
public sealed class WindowGroupService
{
    private const int MaxGroupId = 20;
    private const int MinGroupId = 1;

    private readonly IWindowGroupRepository _repo;
    private readonly IWindowHost _host;
    private readonly WindowLayoutOptions _options;
    private readonly ILogger<WindowGroupService> _logger;

    public WindowGroupService(
        IWindowGroupRepository repo,
        IWindowHost host,
        IOptions<WindowLayoutOptions> options,
        ILogger<WindowGroupService> logger)
    {
        _repo = repo;
        _host = host;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>从仓库加载窗口布局。</summary>
    public WindowLayout Load() => _repo.Load(_options);

    /// <summary>将窗口布局写回仓库（含备份轮转）。</summary>
    public void Save(WindowLayout layout) => _repo.Save(_options, layout);

    /// <summary>
    /// 将合约窗口绑定到指定分组：已存在则改 GroupId（保留位置等属性），否则追加新窗口。
    /// </summary>
    public WindowLayout AssignWindowToGroup(WindowLayout layout, string instrumentCode, int groupId)
    {
        ValidateInstrumentCode(instrumentCode);
        ValidateGroupId(groupId);
        var exists = layout.Windows.Any(w => w.InstrumentCode == instrumentCode);
        var windows = exists
            ? layout.Windows
                .Select(w => w.InstrumentCode == instrumentCode ? w with { GroupId = groupId } : w)
                .ToArray()
            : layout.Windows
                .Append(new InstrumentWindow { InstrumentCode = instrumentCode, GroupId = groupId })
                .ToArray();
        return layout with { Windows = windows };
    }

    /// <summary>
    /// 解绑合约窗口：从布局中移除该窗口（Users.xml 中删除对应 &lt;Instrument&gt; 元素）。
    /// </summary>
    public WindowLayout UnassignWindow(WindowLayout layout, string instrumentCode)
    {
        ValidateInstrumentCode(instrumentCode);
        var windows = layout.Windows.Where(w => w.InstrumentCode != instrumentCode).ToArray();
        return layout with { Windows = windows };
    }

    /// <summary>重命名指定分组（仅改该组 Name，其余组不变）。</summary>
    public WindowLayout RenameGroup(WindowLayout layout, int groupId, string newName)
    {
        ValidateGroupId(groupId);
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("分组名不能为空", nameof(newName));
        var groups = layout.Groups
            .Select(g => g.Id == groupId ? g with { Name = newName } : g)
            .ToArray();
        return layout with { Groups = groups };
    }

    /// <summary>返回指定分组内的全部窗口。</summary>
    public IReadOnlyList<InstrumentWindow> GetWindowsInGroup(WindowLayout layout, int groupId)
    {
        ValidateGroupId(groupId);
        return layout.Windows.Where(w => w.GroupId == groupId).ToArray();
    }

    /// <summary>打开指定分组的全部窗口（一次调用 IWindowHost.OpenGroup，水平紧密排列 + 成组同步）。</summary>
    public void OpenGroup(WindowLayout layout, int groupId)
    {
        ValidateGroupId(groupId);
        var windows = layout.Windows.Where(w => w.GroupId == groupId).ToArray();
        _host.OpenGroup(windows, groupId);
        _logger.LogInformation("已打开分组 {GroupId} 的 {Count} 个窗口", groupId, windows.Length);
    }

    public bool IsWindowOpen(string instrumentCode)
    {
        ValidateInstrumentCode(instrumentCode);
        return _host.IsOpen(instrumentCode);
    }

    public void FocusWindow(string instrumentCode)
    {
        ValidateInstrumentCode(instrumentCode);
        _host.Focus(instrumentCode);
    }

    public void CloseWindow(string instrumentCode)
    {
        ValidateInstrumentCode(instrumentCode);
        _host.Close(instrumentCode);
    }

    private static void ValidateGroupId(int groupId)
    {
        if (groupId < MinGroupId || groupId > MaxGroupId)
            throw new ArgumentOutOfRangeException(nameof(groupId), groupId, "分组号必须在 1-20 之间");
    }

    private static void ValidateInstrumentCode(string instrumentCode)
    {
        if (string.IsNullOrWhiteSpace(instrumentCode))
            throw new ArgumentException("合约码不能为空", nameof(instrumentCode));
    }
}
