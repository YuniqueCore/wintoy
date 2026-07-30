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

    // ── ReorderWindowInGroup（组内上下移动） ─────────────────

    [Fact]
    public void ReorderWindowInGroup_moves_up_within_same_group()
    {
        // 场景：组 1 三个窗口 [ag2608, ag2610, ag2612]；把 ag2610 移到首位
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1, Top = 10 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1, Top = 20 },
                new InstrumentWindow { InstrumentCode = "ag2612", GroupId = 1, Top = 30 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2, Top = 40 }
            ]
        };

        var result = _service.ReorderWindowInGroup(layout, "ag2610", -1);

        // 新顺序：ag2610 升到组 1 的首位，组 2 跨组窗口保持原位
        result.Windows.Select(w => w.InstrumentCode).Should().Equal("ag2610", "ag2608", "ag2612", "cu2609");
        // 字段保持：每个窗口的 Top/GroupId 等不动
        result.Windows.First(w => w.InstrumentCode == "ag2610").Top.Should().Be(20);
        result.Windows.First(w => w.InstrumentCode == "ag2608").Top.Should().Be(10);
    }

    [Fact]
    public void ReorderWindowInGroup_moves_down_within_same_group()
    {
        // 场景：组 1 [ag2608, ag2610, ag2612]；把 ag2610 移到末位
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2612", GroupId = 1 }
            ]
        };

        var result = _service.ReorderWindowInGroup(layout, "ag2610", +1);

        result.Windows.Select(w => w.InstrumentCode).Should().Equal("ag2608", "ag2612", "ag2610");
    }

    [Fact]
    public void ReorderWindowInGroup_at_first_position_moves_up_is_noop()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 }
            ]
        };

        var result = _service.ReorderWindowInGroup(layout, "ag2608", -1);

        // 首位的窗口上移无效 → 顺序保持
        result.Windows.Select(w => w.InstrumentCode).Should().Equal("ag2608", "ag2610");
    }

    [Fact]
    public void ReorderWindowInGroup_at_last_position_moves_down_is_noop()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 }
            ]
        };

        var result = _service.ReorderWindowInGroup(layout, "ag2610", +1);

        result.Windows.Select(w => w.InstrumentCode).Should().Equal("ag2608", "ag2610");
    }

    [Fact]
    public void ReorderWindowInGroup_does_not_cross_groups()
    {
        // 场景：组 1 [ag2608]，组 2 [cu2609]；把 cu2609 移到 -1（试图升到组 1 之上）
        // 但跨组时只看本组子序列 → cu2609 在组 2 内已在首位，noop
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2 }
            ]
        };

        var result = _service.ReorderWindowInGroup(layout, "cu2609", -1);

        // cu2609 在组 2 内已经首位 → noop
        result.Windows.Select(w => w.InstrumentCode).Should().Equal("ag2608", "cu2609");
    }

    [Fact]
    public void ReorderWindowInGroup_throws_for_invalid_delta()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }]
        };

        var act = () => _service.ReorderWindowInGroup(layout, "ag2608", 2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ReorderWindowInGroup_returns_same_layout_when_code_not_found()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }]
        };

        var result = _service.ReorderWindowInGroup(layout, "missing", -1);

        result.Should().BeSameAs(layout);
    }

    [Fact]
    public void CanMoveUp_returns_false_at_first_position()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 }
            ]
        };

        _service.CanMoveUp(layout, "ag2608").Should().BeFalse();
        _service.CanMoveUp(layout, "ag2610").Should().BeTrue();
    }

    [Fact]
    public void CanMoveDown_returns_false_at_last_position()
    {
        var layout = new WindowLayout
        {
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 },
                new InstrumentWindow { InstrumentCode = "ag2610", GroupId = 1 }
            ]
        };

        _service.CanMoveDown(layout, "ag2608").Should().BeTrue();
        _service.CanMoveDown(layout, "ag2610").Should().BeFalse();
    }

    [Fact]
    public void CanMove_returns_false_for_unknown_window()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }]
        };

        _service.CanMoveUp(layout, "missing").Should().BeFalse();
        _service.CanMoveDown(layout, "missing").Should().BeFalse();
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
