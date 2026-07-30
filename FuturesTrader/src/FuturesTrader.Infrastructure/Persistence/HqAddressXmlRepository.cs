using System.Xml.Linq;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Connections;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Infrastructure.Persistence;

/// <summary>
/// HQAddress.xml 仓库：读写行情上游地址列表。
/// <para>XML 格式：<c>&lt;Address Name="海通" Port="38215"&gt;180.168.212.75&lt;/Address&gt;</c></para>
/// </summary>
public sealed class HqAddressXmlRepository : IHqAddressRepository
{
    private readonly ILogger<HqAddressXmlRepository> _logger;

    public HqAddressXmlRepository(ILogger<HqAddressXmlRepository> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<HqAddressEntry> Load(string path)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("HQAddress.xml 不存在：{Path}", path);
            return [];
        }

        var doc = XDocument.Load(path);
        var entries = doc.Root?
            .Elements("Address")
            .Select(el => new HqAddressEntry
            {
                Name = (string?)el.Attribute("Name") ?? string.Empty,
                Host = el.Value.Trim(),
                Port = (int?)el.Attribute("Port") ?? 0
            })
            .ToList() ?? [];

        _logger.LogInformation("加载 {Count} 个行情上游地址", entries.Count);
        return entries;
    }

    /// <inheritdoc />
    public void Save(string path, IReadOnlyList<HqAddressEntry> entries)
    {
        var doc = new XDocument(
            new XElement("HQAddress",
                entries.Select(e => new XElement("Address",
                    new XAttribute("Name", e.Name),
                    new XAttribute("Port", e.Port),
                    e.Host))));

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        doc.Save(path);
        _logger.LogInformation("保存 {Count} 个行情上游地址到 {Path}", entries.Count, path);
    }
}
