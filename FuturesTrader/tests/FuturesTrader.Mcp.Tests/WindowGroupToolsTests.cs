using System.Text.Json;
using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Mcp.Tests;

/// <summary>
/// WindowGroupTools 单元测试：覆盖 5 个 MCP 工具的 list/rename/assign/open/unassign。
/// 用真实 WindowGroupService + Stub 仓库 + Stub 宿主，验证 load→transform→save 链路与 JSON 输出。
/// </summary>
public class WindowGroupToolsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static WindowGroupService NewService(
        out StubWindowGroupRepository repo,
        out StubWindowHost host)
    {
        repo = new StubWindowGroupRepository(new WindowLayout
        {
            UserId = "338897",
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 }
            ],
            Groups = WindowLayout.CreateDefaultGroups()
        });
        host = new StubWindowHost();
        return new WindowGroupService(
            repo,
            host,
            Microsoft.Extensions.Options.Options.Create(new WindowLayoutOptions { UserId = "338897" }),
            NullLogger<WindowGroupService>.Instance);
    }

    // ── list_groups ──────────────────────────────────────────

    [Fact]
    public void ListGroups_returns_json_with_20_groups_and_windows()
    {
        var service = NewService(out _, out _);

        var json = WindowGroupTools.ListGroups(service);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("userId").GetString().Should().Be("338897");
        var groups = doc.RootElement.GetProperty("groups");
        groups.GetArrayLength().Should().Be(20);
        var group1 = groups[0];
        group1.GetProperty("id").GetInt32().Should().Be(1);
        group1.GetProperty("name").GetString().Should().Be("组 1");
        group1.GetProperty("windows").GetArrayLength().Should().Be(2, "组 1 含 ag2608 + ag2610");
        groups[1].GetProperty("windows").GetArrayLength().Should().Be(0, "组 2 无窗口");
    }

    // ── rename_group ─────────────────────────────────────────

    [Fact]
    public void RenameGroup_changes_name_and_persists()
    {
        var service = NewService(out var repo, out _);

        var json = WindowGroupTools.RenameGroup(3, "有色", service);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("name").GetString().Should().Be("有色");
        repo.LastSaved.Should().NotBeNull();
        repo.LastSaved!.Groups.First(g => g.Id == 3).Name.Should().Be("有色");
    }

    // ── assign_window_to_group ───────────────────────────────

    [Fact]
    public void AssignWindowToGroup_adds_window_and_persists()
    {
        var service = NewService(out var repo, out _);

        var json = WindowGroupTools.AssignWindowToGroup("cu2609", 5, service);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("groupId").GetInt32().Should().Be(5);
        doc.RootElement.GetProperty("windowsInGroup").GetInt32().Should().Be(1);
        repo.LastSaved!.Windows.Should().Contain(w => w.InstrumentCode == "cu2609" && w.GroupId == 5);
    }

    // ── open_group ───────────────────────────────────────────

    [Fact]
    public void OpenGroup_calls_host_for_each_window_in_group()
    {
        var service = NewService(out _, out var host);

        var result = WindowGroupTools.OpenGroup(1, service);

        host.Opened.Should().HaveCount(2);
        host.Opened.Select(w => w.InstrumentCode).Should().Equal("ag2608", "ag2610");
        result.Should().Contain("2 个窗口");
    }

    // ── unassign_window ──────────────────────────────────────

    [Fact]
    public void UnassignWindow_removes_window_and_persists()
    {
        var service = NewService(out var repo, out _);

        var json = WindowGroupTools.UnassignWindow("ag2608", service);

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("remainingWindows").GetInt32().Should().Be(1, "移除 ag2608 后剩 ag2610");
        repo.LastSaved!.Windows.Should().NotContain(w => w.InstrumentCode == "ag2608");
        repo.LastSaved.Windows.Should().Contain(w => w.InstrumentCode == "ag2610");
    }

    // ── Stub ─────────────────────────────────────────────────

    private sealed class StubWindowGroupRepository : IWindowGroupRepository
    {
        public WindowLayout Current { get; set; }
        public WindowLayout? LastSaved { get; private set; }

        public StubWindowGroupRepository(WindowLayout initial) => Current = initial;

        public WindowLayout Load(WindowLayoutOptions options) => Current;

        public void Save(WindowLayoutOptions options, WindowLayout layout)
        {
            Current = layout;
            LastSaved = layout;
        }
    }

    private sealed class StubWindowHost : IWindowHost
    {
        private readonly HashSet<string> _open = new();
        public List<InstrumentWindow> Opened { get; } = new();

        public bool IsOpen(string instrumentCode) => _open.Contains(instrumentCode);
        public void Open(InstrumentWindow window) { _open.Add(window.InstrumentCode); Opened.Add(window); }
        public void Focus(string instrumentCode) { }
        public void Close(string instrumentCode) => _open.Remove(instrumentCode);
    }
}
