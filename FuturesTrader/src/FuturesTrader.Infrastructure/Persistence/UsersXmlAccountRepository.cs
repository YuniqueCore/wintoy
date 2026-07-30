using System.Xml.Linq;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Persistence;

/// <summary>
/// Users.xml 账号仓库：读取顶层 &lt;User&gt; 元素的连接信息（title/address/brokerid/userid/appid/shouquan）。
/// 与 <c>UsersXmlWindowGroupRepository</c> 互补：后者读 WindowHistory，本接口读账号凭据。
/// </summary>
public sealed class UsersXmlAccountRepository : IAccountRepository
{
    private readonly ILogger<UsersXmlAccountRepository> _logger;

    public UsersXmlAccountRepository(ILogger<UsersXmlAccountRepository> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<AccountEntry> Load(string usersXmlPath)
    {
        if (!File.Exists(usersXmlPath))
        {
            _logger.LogWarning("Users.xml 不存在：{Path}", usersXmlPath);
            return [];
        }

        var doc = XDocument.Load(usersXmlPath);
        var entries = doc.Root?
            .Elements("User")
            .Select(ParseUserElement)
            .Where(a => !string.IsNullOrEmpty(a.UserId))
            .ToList() ?? [];

        _logger.LogInformation("加载 {Count} 个交易账号", entries.Count);
        return entries;
    }

    /// <inheritdoc />
    public void Add(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));

        var doc = LoadOrCreateDoc(usersXmlPath);
        EnsureRoot(doc);

        if (doc.Root!.Elements("User").Any(u => (string?)u.Element("userid") == account.UserId))
            throw new InvalidOperationException($"UserId 已存在：{account.UserId}");

        var userEl = new XElement("User",
            new XElement("title", account.Title),
            new XElement("address", account.TradingAddress),
            new XElement("brokerid", account.BrokerId),
            new XElement("userid", account.UserId),
            new XElement("appid", account.AppId),
            new XElement("shouquan", account.AuthCode));
        doc.Root.Add(userEl);

        SaveDoc(doc, usersXmlPath);
        _logger.LogInformation("新增账号 {UserId}", account.UserId);
    }

    /// <inheritdoc />
    public void Update(string usersXmlPath, AccountEntry account)
    {
        if (string.IsNullOrWhiteSpace(account.UserId))
            throw new ArgumentException("UserId 不能为空", nameof(account));

        var doc = LoadOrCreateDoc(usersXmlPath);
        EnsureRoot(doc);

        var userEl = doc.Root!.Elements("User")
            .FirstOrDefault(u => (string?)u.Element("userid") == account.UserId)
            ?? throw new InvalidOperationException($"未找到账号 {account.UserId}");

        // 仅更新连接信息，保留 WindowHistory 不动
        SetElement(userEl, "title", account.Title);
        SetElement(userEl, "address", account.TradingAddress);
        SetElement(userEl, "brokerid", account.BrokerId);
        SetElement(userEl, "userid", account.UserId);
        SetElement(userEl, "appid", account.AppId);
        SetElement(userEl, "shouquan", account.AuthCode);

        SaveDoc(doc, usersXmlPath);
        _logger.LogInformation("更新账号 {UserId} 连接信息", account.UserId);
    }

    /// <inheritdoc />
    public void Delete(string usersXmlPath, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("UserId 不能为空", nameof(userId));

        if (!File.Exists(usersXmlPath))
        {
            _logger.LogWarning("Users.xml 不存在，跳过删除：{Path}", usersXmlPath);
            return;
        }

        var doc = XDocument.Load(usersXmlPath);
        var userEl = doc.Root?.Elements("User")
            .FirstOrDefault(u => (string?)u.Element("userid") == userId);
        if (userEl is null)
        {
            _logger.LogInformation("未找到账号 {UserId}，跳过删除（幂等）", userId);
            return;
        }

        userEl.Remove();
        doc.Save(usersXmlPath);
        _logger.LogInformation("删除账号 {UserId}（含 WindowHistory）", userId);
    }

    /// <summary>读取 Users.xml；不存在则创建空文档（带 &lt;Users&gt; 根）。</summary>
    private static XDocument LoadOrCreateDoc(string path) =>
        File.Exists(path) ? XDocument.Load(path) : new XDocument(new XElement("Users"));

    /// <summary>确保根元素是 &lt;Users&gt;，缺失则创建。</summary>
    private static void EnsureRoot(XDocument doc)
    {
        if (doc.Root is null) doc.Add(new XElement("Users"));
    }

    /// <summary>保存文档并确保目录存在（UTF-8 无 BOM + Tab 缩进，匹配原 Users.xml 风格）。</summary>
    private static void SaveDoc(XDocument doc, string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var settings = new System.Xml.XmlWriterSettings
        {
            Encoding = new System.Text.UTF8Encoding(false),
            Indent = true,
            IndentChars = "\t",
        };
        using var writer = System.Xml.XmlWriter.Create(path, settings);
        doc.Save(writer);
    }

    private static AccountEntry ParseUserElement(XElement userEl)
    {
        return new AccountEntry
        {
            Title = (string?)userEl.Element("title") ?? string.Empty,
            TradingAddress = (string?)userEl.Element("address") ?? string.Empty,
            BrokerId = (string?)userEl.Element("brokerid") ?? string.Empty,
            UserId = (string?)userEl.Element("userid") ?? string.Empty,
            AppId = (string?)userEl.Element("appid") ?? string.Empty,
            AuthCode = (string?)userEl.Element("shouquan") ?? string.Empty
        };
    }

    private static void SetElement(XElement parent, string name, string value)
    {
        var el = parent.Element(name);
        if (el is null)
            parent.Add(new XElement(name, value));
        else
            el.Value = value;
    }
}
