using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml;
using System.Xml.Linq;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.WindowGroups;

namespace FuturesTrader.Infrastructure.Persistence.WindowGroups;

/// <summary>
/// 读写旧软件 Users.xml（窗口→分组绑定）+ 旁挂 window-groups.json（20 个组名）。
/// 单类管两文件，KISS：窗口绑定复用 legacy &lt;Instrument Group="N"&gt; 格式保持旧软件兼容，
/// 组名单独存 JSON 不污染 XML。Save 时对两文件各做 .bkp1/.bkp2/.bkp3 滚动备份（保留 3 个）。
/// Users.xml 为 UTF-8（实证），无 BOM；写出用 UTF8Encoding(false) 匹配原文件。
/// </summary>
public sealed class UsersXmlWindowGroupRepository : IWindowGroupRepository
{
    private const int BackupKeep = 3;

    /// <summary>JSON 序列化选项：缩进 + 中文可读（非 \u 转义），与 ConfigRepository 一致。</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>XML 写出设置：UTF-8 无 BOM + Tab 缩进（匹配原 Users.xml 风格）。</summary>
    private static readonly XmlWriterSettings XmlSettings = new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        IndentChars = "\t"
    };

    public WindowLayout Load(WindowLayoutOptions options)
    {
        if (!File.Exists(options.UsersXmlPath))
            throw new FileNotFoundException($"Users.xml 不存在: {options.UsersXmlPath}", options.UsersXmlPath);

        var doc = XDocument.Load(options.UsersXmlPath);
        var user = SelectUserElement(doc, options.UserId);
        var windows = user.Element("WindowHistory")?.Elements("Instrument")
            .Select(ParseInstrument)
            .ToArray() ?? [];
        var groups = LoadGroupNames(options.GroupsJsonPath);
        return new WindowLayout { UserId = options.UserId, Windows = windows, Groups = groups };
    }

    public void Save(WindowLayoutOptions options, WindowLayout layout)
    {
        RotateBackup(options.UsersXmlPath);
        RotateBackup(options.GroupsJsonPath);
        UpdateUsersXml(options.UsersXmlPath, layout);
        SaveGroupNames(options.GroupsJsonPath, layout.Groups);
    }

    // ── XML 读取 ──────────────────────────────────────────────

    /// <summary>
    /// 按 &lt;userid&gt; 选 User：UserId 空或匹配不到则取第一个 User，无 User 抛错。
    /// </summary>
    private static XElement SelectUserElement(XDocument doc, string userId)
    {
        var users = doc.Root?.Elements("User") ?? Enumerable.Empty<XElement>();
        if (!string.IsNullOrEmpty(userId))
        {
            var matched = users.FirstOrDefault(u => (string?)u.Element("userid") == userId);
            if (matched is not null) return matched;
        }
        return users.FirstOrDefault()
            ?? throw new InvalidOperationException("Users.xml 无 <User> 元素");
    }

    private static InstrumentWindow ParseInstrument(XElement el) => new()
    {
        InstrumentCode = el.Value.Trim(),
        GroupId = (int?)el.Attribute("Group") ?? 0,
        Top = (int?)el.Attribute("Top") ?? 0,
        Left = (int?)el.Attribute("Left") ?? 0,
        Height = (int?)el.Attribute("Height") ?? 1000,
        Width = (int?)el.Attribute("Width") ?? 271,
        ValLeft = (int?)el.Attribute("ValLeft") ?? 1,
        ValRight = (int?)el.Attribute("ValRight") ?? 2,
        RowHeight = (int?)el.Attribute("RowHeight") ?? 12,
        RboA = ParseBool(el.Attribute("RBOA")),
        RboB = ParseBool(el.Attribute("RBOB"), defaultValue: true),
        CbNearby = ParseBool(el.Attribute("CBNearby")),
        CbOnlyOpen = ParseBool(el.Attribute("CBOnlyOpen")),
        GroupEx = (int?)el.Attribute("GroupEX") ?? 0,
        CntrbySprdId = (string?)el.Attribute("CntrbySprdID") ?? string.Empty,
        CntrbySprdPt = (int?)el.Attribute("CntrbySprdPT") ?? 0,
        CntrbySprdIdEx = (string?)el.Attribute("CntrbySprdIDEX") ?? string.Empty,
        CntrbySprdPtEx = (int?)el.Attribute("CntrbySprdPTEX") ?? 0,
        CntrbySprdFctn = (int?)el.Attribute("CntrbySprdFctn") ?? 1,
        NarrowMode = ParseBool(el.Attribute("isNarrowMode")),
        CbCntrbySprd = ParseBool(el.Attribute("CBCntrbySprd")),
        CbCntrbySprdEx = ParseBool(el.Attribute("CBCntrbySprdEX")),
        CbCdLock = ParseBool(el.Attribute("CBCDLock")),
        CbBgds = ParseBool(el.Attribute("CBBGDS"), defaultValue: true),
        CbZdtLock = ParseBool(el.Attribute("CBZDTlock"), defaultValue: true)
    };

    private static bool ParseBool(XAttribute? attr, bool defaultValue = false) =>
        attr is null ? defaultValue : (string)attr == "true";

    // ── XML 写出 ──────────────────────────────────────────────

    /// <summary>
    /// 更新 Users.xml：仅替换目标 User 的 &lt;WindowHistory&gt; 下 &lt;Instrument&gt; 子元素，
    /// 保留该 User 的兄弟元素（title/address/brokerid/userid/appid/shouquan）与其他 &lt;User&gt;。
    /// </summary>
    private static void UpdateUsersXml(string path, WindowLayout layout)
    {
        var doc = XDocument.Load(path);
        var user = SelectUserElement(doc, layout.UserId);
        var history = user.Element("WindowHistory");
        if (history is null)
        {
            history = new XElement("WindowHistory");
            user.AddFirst(history);
        }
        else
        {
            history.Elements("Instrument").Remove();
        }
        foreach (var w in layout.Windows)
            history.Add(BuildInstrumentElement(w));
        using var writer = XmlWriter.Create(path, XmlSettings);
        doc.Save(writer);
    }

    /// <summary>构造 &lt;Instrument&gt; 元素：文本=合约码，属性名严格对齐旧软件（RBOA/CBBGDS 等）。</summary>
    private static XElement BuildInstrumentElement(InstrumentWindow w) => new(
        "Instrument",
        w.InstrumentCode,
        new XAttribute("Top", w.Top),
        new XAttribute("Left", w.Left),
        new XAttribute("Height", w.Height),
        new XAttribute("Width", w.Width),
        new XAttribute("ValLeft", w.ValLeft),
        new XAttribute("ValRight", w.ValRight),
        new XAttribute("RowHeight", w.RowHeight),
        new XAttribute("RBOA", BoolStr(w.RboA)),
        new XAttribute("RBOB", BoolStr(w.RboB)),
        new XAttribute("CBNearby", BoolStr(w.CbNearby)),
        new XAttribute("CBOnlyOpen", BoolStr(w.CbOnlyOpen)),
        new XAttribute("Group", w.GroupId),
        new XAttribute("GroupEX", w.GroupEx),
        new XAttribute("CntrbySprdID", w.CntrbySprdId),
        new XAttribute("CntrbySprdPT", w.CntrbySprdPt),
        new XAttribute("CntrbySprdIDEX", w.CntrbySprdIdEx),
        new XAttribute("CntrbySprdPTEX", w.CntrbySprdPtEx),
        new XAttribute("CntrbySprdFctn", w.CntrbySprdFctn),
        new XAttribute("isNarrowMode", BoolStr(w.NarrowMode)),
        new XAttribute("CBCntrbySprd", BoolStr(w.CbCntrbySprd)),
        new XAttribute("CBCntrbySprdEX", BoolStr(w.CbCntrbySprdEx)),
        new XAttribute("CBCDLock", BoolStr(w.CbCdLock)),
        new XAttribute("CBBGDS", BoolStr(w.CbBgds)),
        new XAttribute("CBZDTlock", BoolStr(w.CbZdtLock)));

    private static string BoolStr(bool v) => v ? "true" : "false";

    // ── JSON 组名读写 ─────────────────────────────────────────

    /// <summary>加载 20 个组名：以 CreateDefaultGroups 为基底，用 JSON 中的自定义名覆盖。</summary>
    private static IReadOnlyList<WindowGroup> LoadGroupNames(string path)
    {
        var defaults = WindowLayout.CreateDefaultGroups();
        if (!File.Exists(path)) return defaults;
        try
        {
            var json = File.ReadAllText(path);
            var dtos = JsonSerializer.Deserialize<List<WindowGroup>>(json, JsonOpts);
            if (dtos is null || dtos.Count == 0) return defaults;
            var byId = dtos.Where(d => d.Id is >= 1 and <= 20)
                .GroupBy(d => d.Id)
                .ToDictionary(g => g.Key, g => g.First().Name);
            return defaults
                .Select(g => byId.TryGetValue(g.Id, out var name) ? g with { Name = name } : g)
                .ToArray();
        }
        catch
        {
            // JSON 损坏时回退默认，不阻断加载（窗口绑定仍可正常读写）
            return defaults;
        }
    }

    private static void SaveGroupNames(string path, IReadOnlyList<WindowGroup> groups)
    {
        var json = JsonSerializer.Serialize(groups, JsonOpts);
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    // ── 备份轮转 ──────────────────────────────────────────────

    /// <summary>
    /// 滚动备份：保留最近 BackupKeep 个。删 bkp{N}（超界），bkp2→bkp3, bkp1→bkp2, 当前→bkp1。
    /// 文件不存在则跳过（首次保存无源可备）。
    /// </summary>
    private static void RotateBackup(string filePath)
    {
        if (!File.Exists(filePath)) return;
        // 删除超出保留数量的最旧备份（理论上只有 bkp1/2/3，兜底清理更高序号）
        for (var i = BackupKeep + 1; i <= BackupKeep + 5; i++)
        {
            var extra = $"{filePath}.bkp{i}";
            if (File.Exists(extra)) File.Delete(extra);
        }
        // 从最旧向最新逆向移动：bkp2→bkp3, bkp1→bkp2
        for (var i = BackupKeep - 1; i >= 1; i--)
        {
            var src = $"{filePath}.bkp{i}";
            var dst = $"{filePath}.bkp{i + 1}";
            if (File.Exists(src))
            {
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(src, dst);
            }
        }
        // 当前文件 → bkp1
        File.Copy(filePath, $"{filePath}.bkp1", overwrite: true);
    }
}
