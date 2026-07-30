using FluentAssertions;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Application.Tests.WindowGroups;

/// <summary>
/// WindowGroupService 单元测试：覆盖分配/改组/解绑/重命名/开组/校验/Load-Save 转发。
/// 用内存 Stub 仓库 + Stub 窗口宿主隔离文件与 UI，聚焦业务逻辑。
/// </summary>
public class WindowGroupServiceTests
{
    private readonly StubWindowGroupRepository _repo;
    private readonly StubWindowHost _host;
    private readonly WindowGroupService _service;

    public WindowGroupServiceTests()
    {
        _repo = new StubWindowGroupRepository();
        _host = new StubWindowHost();
        _service = new WindowGroupService(
            _repo,
            _host,
            Microsoft.Extensions.Options.Options.Create(new WindowLayoutOptions { UserId = "test" }),
            NullLogger<WindowGroupService>.Instance);
    }

    // ── AssignWindowToGroup ──────────────────────────────────

    [Fact]
    public void AssignWindowToGroup_appends_new_window_when_not_exists()
    {
        var layout = new WindowLayout();

        var result = _service.AssignWindowToGroup(layout, "ag2608", 1);

        result.Windows.Should().ContainSingle()
            .Which.Should().Match<InstrumentWindow>(w => w.InstrumentCode == "ag2608" && w.GroupId == 1);
    }

    [Fact]
    public void AssignWindowToGroup_updates_existing_window_group_keeping_position()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1, Top = 33, Left = 881 }]
        };

        var result = _service.AssignWindowToGroup(layout, "ag2608", 5);

        result.Windows.Should().ContainSingle()
            .Which.Should().Match<InstrumentWindow>(w => w.GroupId == 5 && w.Top == 33 && w.Left == 881);
    }

    // ── UnassignWindow ───────────────────────────────────────

    [Fact]
    public void UnassignWindow_removes_window_from_layout()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2 }
            ]
        };

        var result = _service.UnassignWindow(layout, "ag2608");

        result.Windows.Should().ContainSingle()
            .Which.InstrumentCode.Should().Be("cu2609");
    }

    [Fact]
    public void UnassignWindow_on_missing_code_is_noop()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }]
        };

        var result = _service.UnassignWindow(layout, "cu2609");

        result.Windows.Should().ContainSingle(w => w.InstrumentCode == "ag2608");
    }

    // ── RenameGroup ──────────────────────────────────────────

    [Fact]
    public void RenameGroup_changes_only_target_group_name()
    {
        var layout = new WindowLayout();

        var result = _service.RenameGroup(layout, 5, "贵金属");

        result.Groups.First(g => g.Id == 5).Name.Should().Be("贵金属");
        result.Groups.First(g => g.Id == 1).Name.Should().Be("组 1", "其余组名不应被改动");
        result.Groups.Should().HaveCount(20);
    }

    [Fact]
    public void RenameGroup_throws_for_empty_name()
    {
        var act = () => _service.RenameGroup(new WindowLayout(), 1, "  ");
        act.Should().Throw<ArgumentException>();
    }

    // ── GetWindowsInGroup / OpenGroup ────────────────────────

    [Fact]
    public void GetWindowsInGroup_returns_only_windows_in_target_group()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2 }
            ]
        };

        var windows = _service.GetWindowsInGroup(layout, 1);

        windows.Should().HaveCount(2);
        windows.Should().OnlyContain(w => w.GroupId == 1);
    }

    [Fact]
    public void OpenGroup_calls_host_open_for_each_window_in_group()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2 }
            ]
        };

        _service.OpenGroup(layout, 1);

        _host.Opened.Should().HaveCount(2);
        _host.Opened.Should().OnlyContain(w => w.GroupId == 1);
    }

    // ── 边界校验 ─────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void AssignWindowToGroup_throws_for_invalid_group_id(int groupId)
    {
        var act = () => _service.AssignWindowToGroup(new WindowLayout(), "ag2608", groupId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void RenameGroup_throws_for_invalid_group_id(int groupId)
    {
        var act = () => _service.RenameGroup(new WindowLayout(), groupId, "x");
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public void OpenGroup_throws_for_invalid_group_id(int groupId)
    {
        var act = () => _service.OpenGroup(new WindowLayout(), groupId);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AssignWindowToGroup_throws_for_empty_code(string code)
    {
        var act = () => _service.AssignWindowToGroup(new WindowLayout(), code, 1);
        act.Should().Throw<ArgumentException>();
    }

    // ── Load / Save 转发 ─────────────────────────────────────

    [Fact]
    public void Load_forwards_to_repository()
    {
        var layout = new WindowLayout { UserId = "x" };
        _repo.Current = layout;

        _service.Load().Should().BeSameAs(layout);
    }

    [Fact]
    public void Save_forwards_to_repository()
    {
        var layout = new WindowLayout { UserId = "y" };

        _service.Save(layout);

        _repo.LastSaved.Should().BeSameAs(layout);
        _repo.SaveCount.Should().Be(1);
    }

    // ── 窗口宿主转发 ─────────────────────────────────────────

    [Fact]
    public void FocusWindow_and_CloseWindow_forward_to_host()
    {
        _host.Open(new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 });
        _host.IsOpen("ag2608").Should().BeTrue();

        _service.FocusWindow("ag2608");
        _host.Focused.Should().Contain("ag2608");

        _service.CloseWindow("ag2608");
        _host.IsOpen("ag2608").Should().BeFalse();
        _host.Closed.Should().Contain("ag2608");
    }

    // ── Stub ─────────────────────────────────────────────────

    /// <summary>内存 Stub 仓库：Load 返回 Current，Save 记录最后保存值并更新 Current。</summary>
    private sealed class StubWindowGroupRepository : IWindowGroupRepository
    {
        public WindowLayout Current { get; set; } = new();
        public WindowLayout? LastSaved { get; private set; }
        public int SaveCount { get; private set; }

        public WindowLayout Load(WindowLayoutOptions options) => Current;

        public void Save(WindowLayoutOptions options, WindowLayout layout)
        {
            Current = layout;
            LastSaved = layout;
            SaveCount++;
        }
    }

    /// <summary>内存 Stub 窗口宿主：HashSet 跟踪已开窗口，记录 Open/Focus/Close 调用。</summary>
    private sealed class StubWindowHost : IWindowHost
    {
        private readonly HashSet<string> _open = new();
        public List<InstrumentWindow> Opened { get; } = new();
        public List<string> Focused { get; } = new();
        public List<string> Closed { get; } = new();

        public bool IsOpen(string instrumentCode) => _open.Contains(instrumentCode);

        public void Open(InstrumentWindow window)
        {
            _open.Add(window.InstrumentCode);
            Opened.Add(window);
        }

        public void OpenGroup(IReadOnlyList<InstrumentWindow> windows, int groupId)
        {
            foreach (var w in windows) Open(w);
        }

        public IReadOnlyList<string> GetOpenWindowsInGroup(int groupId) => _open.ToList();

        public void CloseGroup(int groupId)
        {
            foreach (var code in _open.ToList()) Close(code);
        }

        public void Focus(string instrumentCode) => Focused.Add(instrumentCode);

        public void Close(string instrumentCode)
        {
            _open.Remove(instrumentCode);
            Closed.Add(instrumentCode);
        }
    }
}
