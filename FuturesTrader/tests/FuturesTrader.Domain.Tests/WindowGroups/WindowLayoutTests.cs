using FluentAssertions;
using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Domain.Tests.WindowGroups;

/// <summary>
/// WindowLayout/WindowGroup/InstrumentWindow 领域模型测试：
/// 默认 20 组命名、InstrumentWindow 默认值对齐 Users.xml、record with 不可变性。
/// </summary>
public class WindowLayoutTests
{
    // ── CreateDefaultGroups ───────────────────────────────────

    [Fact]
    public void CreateDefaultGroups_returns_20_groups_with_default_names()
    {
        var groups = WindowLayout.CreateDefaultGroups();

        groups.Should().HaveCount(20);
        groups.Select(g => g.Id).Should().Equal(Enumerable.Range(1, 20));
        groups.Select(g => g.Name).Should().Equal(Enumerable.Range(1, 20).Select(i => $"组 {i}"));
    }

    [Fact]
    public void WindowLayout_default_has_empty_windows_and_20_groups()
    {
        var layout = new WindowLayout();

        layout.UserId.Should().BeEmpty();
        layout.Windows.Should().BeEmpty();
        layout.Groups.Should().HaveCount(20);
    }

    // ── InstrumentWindow 默认值对齐 Users.xml ─────────────────

    [Fact]
    public void InstrumentWindow_defaults_match_users_xml_empirical_values()
    {
        var w = new InstrumentWindow();

        w.InstrumentCode.Should().BeEmpty();
        w.GroupId.Should().Be(0, "未分组默认 0");
        w.Height.Should().Be(1000);
        w.Width.Should().Be(271);
        w.ValLeft.Should().Be(1);
        w.ValRight.Should().Be(2);
        w.RowHeight.Should().Be(12);
        w.RboA.Should().BeFalse();
        w.RboB.Should().BeTrue("Users.xml ag/jd 族 RBOB=true");
        w.CntrbySprdFctn.Should().Be(1);
        w.CbBgds.Should().BeTrue();
        w.CbZdtLock.Should().BeTrue();
    }

    // ── record with 不可变性 ──────────────────────────────────

    [Fact]
    public void WindowGroup_with_produces_new_instance_leaving_original_unchanged()
    {
        var original = new WindowGroup { Id = 1, Name = "组 1" };
        var renamed = original with { Name = "贵金属" };

        renamed.Name.Should().Be("贵金属");
        original.Name.Should().Be("组 1", "原 record 不应被改动");
        renamed.Id.Should().Be(1);
    }

    [Fact]
    public void InstrumentWindow_with_changes_only_specified_field()
    {
        var original = new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1, Top = 33, Left = 881 };
        var moved = original with { GroupId = 5 };

        moved.GroupId.Should().Be(5);
        moved.InstrumentCode.Should().Be("ag2608");
        moved.Top.Should().Be(33, "未改字段应保留");
        moved.Left.Should().Be(881);
    }

    [Fact]
    public void WindowLayout_with_replaces_windows_collection()
    {
        var layout = new WindowLayout
        {
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }]
        };
        var updated = layout with
        {
            Windows = layout.Windows.Append(new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 2 }).ToArray()
        };

        updated.Windows.Should().HaveCount(2);
        layout.Windows.Should().HaveCount(1, "原 layout 不应被改动");
    }
}
