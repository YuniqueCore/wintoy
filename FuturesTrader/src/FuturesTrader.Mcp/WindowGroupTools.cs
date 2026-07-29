using System.ComponentModel;
using System.Text.Json;
using System.Text.Unicode;
using FuturesTrader.Application;
using ModelContextProtocol.Server;

namespace FuturesTrader.Mcp;

/// <summary>
/// MCP 工具集：暴露窗口分组管理能力给外部 agent。
/// 注入 <see cref="WindowGroupService"/>，每个工具 load→transform→save 无状态编排。
/// 与 <see cref="ConfigTools"/> 同程序集，被 WithToolsFromAssembly() 自动发现，无需改扩展方法。
/// </summary>
[McpServerToolType]
public static class WindowGroupTools
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>列出全部 20 个分组及其绑定的合约窗口。</summary>
    [McpServerTool, Description("列出全部 20 个分组及其绑定的合约窗口。")]
    public static string ListGroups(WindowGroupService service)
    {
        var layout = service.Load();
        return JsonSerializer.Serialize(new
        {
            userId = layout.UserId,
            groups = layout.Groups.Select(g => new
            {
                id = g.Id,
                name = g.Name,
                windows = layout.Windows
                    .Where(w => w.GroupId == g.Id)
                    .Select(w => w.InstrumentCode)
                    .ToArray()
            })
        }, JsonOpts);
    }

    /// <summary>重命名指定分组（groupId 1-20）。</summary>
    [McpServerTool, Description("重命名指定分组（groupId 1-20，newName 非空）。")]
    public static string RenameGroup(
        int groupId,
        string newName,
        WindowGroupService service)
    {
        var layout = service.Load();
        layout = service.RenameGroup(layout, groupId, newName);
        service.Save(layout);
        return JsonSerializer.Serialize(new
        {
            groupId,
            name = layout.Groups.First(g => g.Id == groupId).Name
        }, JsonOpts);
    }

    /// <summary>将合约窗口绑定到指定分组（已存在则改组，否则追加）。</summary>
    [McpServerTool, Description("将合约窗口绑定到指定分组（instrumentCode 如 ag2608，groupId 1-20）。已存在则改组，否则追加。")]
    public static string AssignWindowToGroup(
        string instrumentCode,
        int groupId,
        WindowGroupService service)
    {
        var layout = service.Load();
        layout = service.AssignWindowToGroup(layout, instrumentCode, groupId);
        service.Save(layout);
        return JsonSerializer.Serialize(new
        {
            instrumentCode,
            groupId,
            windowsInGroup = layout.Windows.Count(w => w.GroupId == groupId)
        }, JsonOpts);
    }

    /// <summary>打开指定分组的全部窗口。</summary>
    [McpServerTool, Description("打开指定分组（groupId 1-20）的全部合约窗口。")]
    public static string OpenGroup(int groupId, WindowGroupService service)
    {
        var layout = service.Load();
        service.OpenGroup(layout, groupId);
        var count = layout.Windows.Count(w => w.GroupId == groupId);
        return $"已打开分组 {groupId} 的 {count} 个窗口";
    }

    /// <summary>解绑合约窗口（从其分组移除并删除 Users.xml 记录）。</summary>
    [McpServerTool, Description("解绑合约窗口：从分组移除并删除 Users.xml 中的记录。")]
    public static string UnassignWindow(string instrumentCode, WindowGroupService service)
    {
        var layout = service.Load();
        layout = service.UnassignWindow(layout, instrumentCode);
        service.Save(layout);
        return JsonSerializer.Serialize(new
        {
            instrumentCode,
            remainingWindows = layout.Windows.Count
        }, JsonOpts);
    }
}
