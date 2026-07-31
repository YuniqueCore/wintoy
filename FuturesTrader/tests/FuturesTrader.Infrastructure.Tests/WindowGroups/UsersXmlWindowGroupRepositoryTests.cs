using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Domain.WindowGroups;
using FuturesTrader.Infrastructure.Persistence.WindowGroups;

namespace FuturesTrader.Infrastructure.Tests.WindowGroups;

/// <summary>
/// UsersXmlWindowGroupRepository 单元测试：覆盖 XML/JSON 读写、按 userid 选 User、
/// 兄弟元素/多 User 保留、备份轮转（bkp1/2/3）、无 BOM、组名合并。用真实临时文件（UTF-8）。
/// </summary>
public class UsersXmlWindowGroupRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public UsersXmlWindowGroupRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ft_wg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Load ──────────────────────────────────────────────────

    [Fact]
    public void Load_parses_instrument_and_all_attributes()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var repo = new UsersXmlWindowGroupRepository();

        var layout = repo.Load(Opts(xmlPath, "338897"));

        layout.Windows.Should().ContainSingle();
        var w = layout.Windows[0];
        w.InstrumentCode.Should().Be("ag2608");
        w.GroupId.Should().Be(1);
        w.Top.Should().Be(33);
        w.Left.Should().Be(881);
        w.Height.Should().Be(1306);
        w.Width.Should().Be(271);
        w.RboA.Should().BeFalse();
        w.RboB.Should().BeTrue();
        w.CbBgds.Should().BeTrue();
        w.CbZdtLock.Should().BeTrue();
        w.CntrbySprdId.Should().Be("ag");
        w.CntrbySprdFctn.Should().Be(1);
    }

    [Fact]
    public void Load_throws_when_users_xml_missing()
    {
        var repo = new UsersXmlWindowGroupRepository();
        var act = () => repo.Load(Opts(Path.Combine(_tempDir, "nope.xml"), "338897"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_selects_user_by_userid()
    {
        var xmlPath = WriteFile("users.xml", MultiUserXml);
        var repo = new UsersXmlWindowGroupRepository();

        var layout = repo.Load(Opts(xmlPath, "222"));

        layout.Windows.Should().ContainSingle(w => w.InstrumentCode == "cu2609" && w.GroupId == 2);
    }

    [Fact]
    public void Load_empty_userid_takes_first_user()
    {
        var xmlPath = WriteFile("users.xml", MultiUserXml);
        var repo = new UsersXmlWindowGroupRepository();

        var layout = repo.Load(Opts(xmlPath, ""));

        layout.Windows.Should().ContainSingle(w => w.InstrumentCode == "ag2608");
    }

    [Fact]
    public void Load_userid_not_found_falls_back_to_first_user()
    {
        var xmlPath = WriteFile("users.xml", MultiUserXml);
        var repo = new UsersXmlWindowGroupRepository();

        var layout = repo.Load(Opts(xmlPath, "999"));

        layout.Windows.Should().ContainSingle(w => w.InstrumentCode == "ag2608", "匹配不到应取首 User");
    }

    [Fact]
    public void Load_missing_json_uses_default_group_names()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var repo = new UsersXmlWindowGroupRepository();

        var layout = repo.Load(Opts(xmlPath, "338897"));

        layout.Groups.Should().HaveCount(20);
        layout.Groups.First(g => g.Id == 1).Name.Should().Be("组 1");
    }

    [Fact]
    public void Load_merges_json_group_names_over_defaults()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = WriteFile("groups.json",
            "[{\"Id\":1,\"Name\":\"贵金属\"},{\"Id\":2,\"Name\":\"黑色\"}]");

        var repo = new UsersXmlWindowGroupRepository();
        var layout = repo.Load(Opts(xmlPath, "338897", jsonPath));

        layout.Groups.First(g => g.Id == 1).Name.Should().Be("贵金属");
        layout.Groups.First(g => g.Id == 2).Name.Should().Be("黑色");
        layout.Groups.First(g => g.Id == 3).Name.Should().Be("组 3", "未自定义的组用默认名");
        layout.Groups.Should().HaveCount(20);
    }

    // ── Save round-trip ──────────────────────────────────────

    [Fact]
    public void Save_then_load_round_trips_windows_and_group_names()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();
        var layout = new WindowLayout
        {
            UserId = "338897",
            Windows =
            [
                new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1, Top = 10, Left = 20 },
                new InstrumentWindow { InstrumentCode = "cu2609", GroupId = 5, Top = 30, Left = 40 }
            ],
            Groups = WindowLayout.CreateDefaultGroups()
        };

        repo.Save(Opts(xmlPath, "338897", jsonPath), layout);
        var reloaded = repo.Load(Opts(xmlPath, "338897", jsonPath));

        reloaded.Windows.Should().HaveCount(2);
        reloaded.Windows.Should().Contain(w => w.InstrumentCode == "ag2608" && w.GroupId == 1 && w.Top == 10);
        reloaded.Windows.Should().Contain(w => w.InstrumentCode == "cu2609" && w.GroupId == 5 && w.Top == 30);
    }

    [Fact]
    public void Save_preserves_sibling_elements_and_other_users()
    {
        var xmlPath = WriteFile("users.xml", MultiUserXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();
        var opts = Opts(xmlPath, "111", jsonPath);

        repo.Save(opts, new WindowLayout
        {
            UserId = "111",
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", GroupId = 1 }],
            Groups = WindowLayout.CreateDefaultGroups()
        });

        var doc = XDocument.Load(xmlPath);
        var users = doc.Root!.Elements("User").ToList();
        users.Should().HaveCount(2, "未操作的 User 应原样保留");

        var user111 = users.First(u => (string?)u.Element("userid") == "111");
        user111.Element("address").Should().NotBeNull("兄弟元素 address 应保留");
        user111.Element("title").Should().NotBeNull();
        user111.Element("WindowHistory")!.Elements("Instrument").Should().HaveCount(1);

        var user222 = users.First(u => (string?)u.Element("userid") == "222");
        user222.Element("WindowHistory")!.Elements("Instrument").Should().HaveCount(1,
            "未操作的 User 的窗口历史不应被改动");
        user222.Element("userid")!.Value.Should().Be("222");
    }

    [Fact]
    public void Save_writes_users_xml_without_bom()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();

        repo.Save(Opts(xmlPath, "338897", jsonPath), new WindowLayout
        {
            UserId = "338897",
            Windows = [],
            Groups = WindowLayout.CreateDefaultGroups()
        });

        var bytes = File.ReadAllBytes(xmlPath);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        hasBom.Should().BeFalse("Users.xml 不应含 BOM（UTF8Encoding(false)）");
    }

    // ── 备份轮转 ─────────────────────────────────────────────

    [Fact]
    public void Save_creates_bkp1_on_first_save()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();

        repo.Save(Opts(xmlPath, "338897", jsonPath), new WindowLayout
        {
            UserId = "338897",
            Groups = WindowLayout.CreateDefaultGroups()
        });

        File.Exists($"{xmlPath}.bkp1").Should().BeTrue("首次保存应生成 bkp1");
    }

    [Fact]
    public void Repeated_save_keeps_only_three_backups()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();
        var opts = Opts(xmlPath, "338897", jsonPath);

        for (var i = 0; i < 4; i++)
        {
            repo.Save(opts, new WindowLayout
            {
                UserId = "338897",
                Windows = [new InstrumentWindow { InstrumentCode = $"p{i}", GroupId = 1 }],
                Groups = WindowLayout.CreateDefaultGroups()
            });
        }

        File.Exists($"{xmlPath}.bkp1").Should().BeTrue();
        File.Exists($"{xmlPath}.bkp2").Should().BeTrue();
        File.Exists($"{xmlPath}.bkp3").Should().BeTrue();
        File.Exists($"{xmlPath}.bkp4").Should().BeFalse("最多保留 3 个备份");
    }

    [Fact]
    public void Rename_persists_to_json_and_survives_reload()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var jsonPath = Path.Combine(_tempDir, "groups.json");
        var repo = new UsersXmlWindowGroupRepository();
        var opts = Opts(xmlPath, "338897", jsonPath);

        var layout = new WindowLayout
        {
            UserId = "338897",
            Windows = [],
            Groups = WindowLayout.CreateDefaultGroups().Select(g => g.Id == 7 ? g with { Name = "有色金属" } : g).ToArray()
        };
        repo.Save(opts, layout);

        File.Exists(jsonPath).Should().BeTrue("组名应写入 window-groups.json");
        var reloaded = repo.Load(opts);
        reloaded.Groups.First(g => g.Id == 7).Name.Should().Be("有色金属", "组名应持久化并在重新加载后保留");
    }

    // ── RunMode 条件字段 ─────────────────────────────────────

    [Fact]
    public void Load_ignores_CBOC_outside_the_proven_run_mode_persistence_branch()
    {
        var xml = SampleUsersXml.Replace(
            "CBOnlyOpen=\"false\"",
            "CBOnlyOpen=\"false\" CBOC=\"true\"",
            StringComparison.Ordinal);
        var xmlPath = WriteFile("users.xml", xml);

        var standard = new UsersXmlWindowGroupRepository(new LegacyTradingRuntime(0));
        var persisted = new UsersXmlWindowGroupRepository(new LegacyTradingRuntime(1));

        standard.Load(Opts(xmlPath, "338897")).Windows[0].CbOc.Should().BeFalse();
        persisted.Load(Opts(xmlPath, "338897")).Windows[0].CbOc.Should().BeTrue();
    }

    [Fact]
    public void Save_run_mode_one_writes_CBOC_but_not_the_other_run_mode_field_family()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var repo = new UsersXmlWindowGroupRepository(new LegacyTradingRuntime(1));
        var layout = new WindowLayout
        {
            UserId = "338897",
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", CbOc = true, CbBgds = true, CbZdtLock = true }],
            Groups = WindowLayout.CreateDefaultGroups()
        };

        repo.Save(Opts(xmlPath, "338897"), layout);

        var instrument = XDocument.Load(xmlPath).Descendants("Instrument").Single();
        ((string?)instrument.Attribute("CBOC")).Should().Be("true");
        instrument.Attribute("CBBGDS").Should().BeNull();
        instrument.Attribute("CBZDTlock").Should().BeNull();
    }

    [Fact]
    public void Save_standard_run_mode_omits_CBOC_and_writes_quote_lock_fields()
    {
        var xmlPath = WriteFile("users.xml", SampleUsersXml);
        var repo = new UsersXmlWindowGroupRepository(new LegacyTradingRuntime(0));
        var layout = new WindowLayout
        {
            UserId = "338897",
            Windows = [new InstrumentWindow { InstrumentCode = "ag2608", CbOc = true, CbBgds = false, CbZdtLock = false }],
            Groups = WindowLayout.CreateDefaultGroups()
        };

        repo.Save(Opts(xmlPath, "338897"), layout);

        var instrument = XDocument.Load(xmlPath).Descendants("Instrument").Single();
        instrument.Attribute("CBOC").Should().BeNull();
        ((string?)instrument.Attribute("CBBGDS")).Should().Be("false");
        ((string?)instrument.Attribute("CBZDTlock")).Should().Be("false");
    }

    // ── 辅助 ─────────────────────────────────────────────────

    private static WindowLayoutOptions Opts(string xmlPath, string userId, string? jsonPath = null) =>
        new() { UsersXmlPath = xmlPath, GroupsJsonPath = jsonPath ?? Path.Combine(Path.GetDirectoryName(xmlPath)!, "groups.json"), UserId = userId };

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>单 User 的 Users.xml 样本（含全部 Instrument 属性，对齐 qihuo-software/Users.xml）。</summary>
    private const string SampleUsersXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Users>
          <User>
            <WindowHistory>
              <Instrument Top="33" Left="881" Height="1306" Width="271" ValLeft="1" ValRight="2" RowHeight="10" RBOA="false" RBOB="true" CBNearby="false" CBOnlyOpen="false" Group="1" GroupEX="0" CntrbySprdID="ag" CntrbySprdPT="0" CntrbySprdIDEX="ag" CntrbySprdPTEX="0" CntrbySprdFctn="1" isNarrowMode="false" CBCntrbySprd="false" CBCntrbySprdEX="false" CBCDLock="false" CBBGDS="true" CBZDTlock="true">ag2608</Instrument>
            </WindowHistory>
            <title>338897</title>
            <address>tcp://1.2.3.4:5</address>
            <brokerid>88888</brokerid>
            <userid>338897</userid>
            <appid>app</appid>
            <shouquan>v1</shouquan>
          </User>
        </Users>
        """;

    /// <summary>双 User 的 Users.xml 样本（userid 111 + 222），用于多 User 选择/保留测试。</summary>
    private const string MultiUserXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Users>
          <User>
            <WindowHistory>
              <Instrument Top="0" Left="0" Height="1000" Width="271" Group="1">ag2608</Instrument>
            </WindowHistory>
            <title>111</title>
            <address>tcp://1.1.1.1:1</address>
            <userid>111</userid>
          </User>
          <User>
            <WindowHistory>
              <Instrument Top="0" Left="0" Height="1000" Width="271" Group="2">cu2609</Instrument>
            </WindowHistory>
            <title>222</title>
            <address>tcp://2.2.2.2:2</address>
            <userid>222</userid>
          </User>
        </Users>
        """;
}
