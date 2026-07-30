using FluentAssertions;
using FuturesTrader.Domain.Connections;
using FuturesTrader.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Infrastructure.Tests.Accounts;

/// <summary>
/// UsersXmlAccountRepository 单元测试：覆盖 Load / Add / Update / Delete 全 CRUD 路径，
/// 包括 UserId 唯一性校验、空 UserId 校验、文件不存在与不存在的 UserId 幂等处理。
/// 用真实临时文件（UTF-8，无 BOM）验证 XML 落盘格式。
/// </summary>
public class UsersXmlAccountRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly UsersXmlAccountRepository _repo = new(NullLogger<UsersXmlAccountRepository>.Instance);

    public UsersXmlAccountRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ft_acct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_tempDir, name);

    private static AccountEntry Sample(string userId, string brokerId = "88888", string title = "338897") => new()
    {
        Title = title,
        TradingAddress = "tcp://122.224.130.77:42205",
        BrokerId = brokerId,
        UserId = userId,
        AppId = "Weg_yiyisy_V1.0",
        AuthCode = "AUTHCODE123",
    };

    // ── Load ──────────────────────────────────────────────────

    [Fact]
    public void Load_returns_empty_when_file_missing()
    {
        var list = _repo.Load(PathOf("missing.xml"));
        list.Should().BeEmpty();
    }

    [Fact]
    public void Load_parses_all_user_elements_with_connection_fields()
    {
        var xmlPath = WriteUsersXml(PathOf("two.xml"),
            Sample("338897", "88888", "Account1"),
            Sample("666666", "99999", "Account2"));

        var list = _repo.Load(xmlPath);

        list.Should().HaveCount(2);
        list[0].UserId.Should().Be("338897");
        list[0].BrokerId.Should().Be("88888");
        list[0].AuthCode.Should().Be("AUTHCODE123");
        list[1].UserId.Should().Be("666666");
    }

    // ── Add ───────────────────────────────────────────────────

    [Fact]
    public void Add_appends_user_element_with_all_connection_fields()
    {
        var xmlPath = PathOf("add.xml");

        _repo.Add(xmlPath, Sample("111111", "88888", "NewAccount"));

        var list = _repo.Load(xmlPath);
        list.Should().ContainSingle()
            .Which.Should().Match<AccountEntry>(a =>
                a.UserId == "111111" &&
                a.BrokerId == "88888" &&
                a.Title == "NewAccount" &&
                a.TradingAddress == "tcp://122.224.130.77:42205" &&
                a.AppId == "Weg_yiyisy_V1.0" &&
                a.AuthCode == "AUTHCODE123");
    }

    [Fact]
    public void Add_creates_file_with_users_root_when_missing()
    {
        var xmlPath = PathOf("created.xml");

        _repo.Add(xmlPath, Sample("222222"));

        File.Exists(xmlPath).Should().BeTrue();
        var content = File.ReadAllText(xmlPath);
        content.Should().Contain("<Users>");
        content.Should().Contain("<userid>222222</userid>");
    }

    [Fact]
    public void Add_throws_when_userid_already_exists()
    {
        var xmlPath = WriteUsersXml(PathOf("dup.xml"), Sample("338897"));

        var act = () => _repo.Add(xmlPath, Sample("338897"));
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*338897*");
    }

    [Fact]
    public void Add_throws_for_empty_userid()
    {
        var act = () => _repo.Add(PathOf("any.xml"), Sample(""));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Add_then_Add_preserves_first_account()
    {
        var xmlPath = PathOf("multi.xml");

        _repo.Add(xmlPath, Sample("111111"));
        _repo.Add(xmlPath, Sample("222222"));

        var list = _repo.Load(xmlPath);
        list.Should().HaveCount(2);
        list.Select(a => a.UserId).Should().Equal("111111", "222222");
    }

    // ── Update ────────────────────────────────────────────────

    [Fact]
    public void Update_modifies_existing_accounts_connection_fields()
    {
        var xmlPath = WriteUsersXml(PathOf("upd.xml"), Sample("338897", "88888", "OldTitle"));

        _repo.Update(xmlPath, Sample("338897", "99999", "NewTitle"));

        var list = _repo.Load(xmlPath);
        list.Should().ContainSingle()
            .Which.Should().Match<AccountEntry>(a =>
                a.UserId == "338897" &&
                a.BrokerId == "99999" &&
                a.Title == "NewTitle");
    }

    [Fact]
    public void Update_throws_when_userid_not_found()
    {
        var xmlPath = WriteUsersXml(PathOf("upd_missing.xml"), Sample("338897"));

        var act = () => _repo.Update(xmlPath, Sample("999999"));
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*999999*");
    }

    [Fact]
    public void Update_throws_for_empty_userid()
    {
        var act = () => _repo.Update(PathOf("any.xml"), Sample(""));
        act.Should().Throw<ArgumentException>();
    }

    // ── Delete ────────────────────────────────────────────────

    [Fact]
    public void Delete_removes_user_element()
    {
        var xmlPath = WriteUsersXml(PathOf("del.xml"),
            Sample("338897"),
            Sample("666666"));

        _repo.Delete(xmlPath, "338897");

        var list = _repo.Load(xmlPath);
        list.Should().ContainSingle()
            .Which.UserId.Should().Be("666666");
    }

    [Fact]
    public void Delete_is_noop_for_missing_userid()
    {
        var xmlPath = WriteUsersXml(PathOf("del_missing.xml"), Sample("338897"));

        _repo.Delete(xmlPath, "999999");  // 不应抛

        var list = _repo.Load(xmlPath);
        list.Should().ContainSingle().Which.UserId.Should().Be("338897");
    }

    [Fact]
    public void Delete_is_noop_when_file_missing()
    {
        var act = () => _repo.Delete(PathOf("no_file.xml"), "338897");
        act.Should().NotThrow();
    }

    [Fact]
    public void Delete_throws_for_empty_userid()
    {
        var act = () => _repo.Delete(PathOf("any.xml"), "");
        act.Should().Throw<ArgumentException>();
    }

    // ── 备份文件格式校验 ──────────────────────────────────────

    [Fact]
    public void Add_writes_utf8_without_bom()
    {
        var xmlPath = PathOf("bom.xml");

        _repo.Add(xmlPath, Sample("338897"));

        var bytes = File.ReadAllBytes(xmlPath);
        // UTF-8 BOM 是 0xEF 0xBB 0xBF
        if (bytes.Length >= 3)
            bytes[..3].Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "必须无 BOM 写入");
    }

    // ── 辅助方法 ──────────────────────────────────────────────

    private static string WriteUsersXml(string path, params AccountEntry[] accounts)
    {
        var root = new System.Xml.Linq.XElement("Users");
        foreach (var a in accounts)
        {
            var user = new System.Xml.Linq.XElement("User");
            user.Add(new System.Xml.Linq.XElement("title", a.Title));
            user.Add(new System.Xml.Linq.XElement("address", a.TradingAddress));
            user.Add(new System.Xml.Linq.XElement("brokerid", a.BrokerId));
            user.Add(new System.Xml.Linq.XElement("userid", a.UserId));
            user.Add(new System.Xml.Linq.XElement("appid", a.AppId));
            user.Add(new System.Xml.Linq.XElement("shouquan", a.AuthCode));
            root.Add(user);
        }
        var doc = new System.Xml.Linq.XDocument(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        doc.Save(path);
        return path;
    }
}
