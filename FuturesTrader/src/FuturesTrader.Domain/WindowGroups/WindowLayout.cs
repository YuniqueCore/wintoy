namespace FuturesTrader.Domain.WindowGroups;

/// <summary>
/// 窗口布局聚合根：某用户的全部合约窗口绑定 + 20 个分组定义。
/// Windows 来自 Users.xml 的 &lt;WindowHistory&gt;，Groups 的 Name 来自 window-groups.json。
/// 组号边界校验放服务层（record 不校验，与现有 WindowConfig 一致）。
/// </summary>
public sealed record WindowLayout
{
    /// <summary>用户 ID（对应 Users.xml 的 &lt;userid&gt;，空串表示取第一个 User）。</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>该用户绑定的全部合约窗口（每窗含 GroupId 指明所属分组）。</summary>
    public IReadOnlyList<InstrumentWindow> Windows { get; init; } = [];

    /// <summary>20 个分组定义（Id 1-20 + 用户可重命名的 Name）。</summary>
    public IReadOnlyList<WindowGroup> Groups { get; init; } = CreateDefaultGroups();

    /// <summary>生成 20 个默认分组："组 1".."组 20"。</summary>
    public static IReadOnlyList<WindowGroup> CreateDefaultGroups() =>
        Enumerable.Range(1, 20).Select(i => new WindowGroup { Id = i, Name = $"组 {i}" }).ToArray();
}
