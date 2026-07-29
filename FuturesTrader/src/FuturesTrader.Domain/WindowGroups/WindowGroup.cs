namespace FuturesTrader.Domain.WindowGroups;

/// <summary>
/// 窗口分组：1-20 号分组之一，Id 为分组号，Name 为用户可重命名的显示名。
/// 与旧软件 Users.xml 的 <Instrument Group="1"> 属性对应：旧软件只有数字 Group，
/// 本模型新增 Name 以支持重命名（Name 旁挂在 window-groups.json，不污染 legacy XML）。
/// </summary>
public sealed record WindowGroup
{
    /// <summary>分组号，范围 1-20（边界校验在 WindowGroupService.ValidateGroupId）。</summary>
    public int Id { get; init; }

    /// <summary>分组显示名，默认 "组 N"。用户可重命名。</summary>
    public string Name { get; init; } = string.Empty;
}
