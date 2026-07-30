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
    public void Save(string usersXmlPath, AccountEntry account)
    {
        if (!File.Exists(usersXmlPath))
        {
            _logger.LogWarning("Users.xml 不存在，无法保存：{Path}", usersXmlPath);
            return;
        }

        var doc = XDocument.Load(usersXmlPath);
        var userEl = doc.Root?
            .Elements("User")
            .FirstOrDefault(u => (string?)u.Element("userid") == account.UserId);

        if (userEl is null)
        {
            _logger.LogWarning("未找到账号 {UserId}，无法保存", account.UserId);
            return;
        }

        // 仅更新连接信息，保留 WindowHistory 不动
        SetElement(userEl, "title", account.Title);
        SetElement(userEl, "address", account.TradingAddress);
        SetElement(userEl, "brokerid", account.BrokerId);
        SetElement(userEl, "userid", account.UserId);
        SetElement(userEl, "appid", account.AppId);
        SetElement(userEl, "shouquan", account.AuthCode);

        doc.Save(usersXmlPath);
        _logger.LogInformation("保存账号 {UserId} 连接信息", account.UserId);
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
