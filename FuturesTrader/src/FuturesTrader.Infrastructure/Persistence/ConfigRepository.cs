using System.Globalization;
using System.Text;
using System.Text.Json;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Infrastructure.Persistence;

/// <summary>
/// 读写旧软件的 GBK 编码 config.ini，映射到 <see cref="CloudConfig"/> 领域模型。
/// INI 格式：[Section] + Key=Value，值含前导空格需 Trim，键名大小写不敏感，重复键后值覆盖。
/// 实现 <see cref="IConfigRepository"/>；ToJson/FromJson 为迁移工具专用，不进接口。
/// </summary>
public sealed class ConfigRepository : IConfigRepository
{
    /// <summary>GBK (Codepage 936) 编码，旧软件 config.ini 的原始编码。</summary>
    private static readonly Encoding Gbk = InitGbkEncoding();

    /// <summary>.NET 10 默认不注册 GBK (codepage 936)，需显式注册 CodePagesEncodingProvider。</summary>
    private static Encoding InitGbkEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(936);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(
            System.Text.Unicode.UnicodeRanges.All)
    };

    /// <summary>从 GBK 编码的 config.ini 加载完整配置。</summary>
    public CloudConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"配置文件不存在: {path}", path);

        var lines = File.ReadAllLines(path, Gbk);
        var ini = ParseIni(lines);
        return MapToConfig(ini);
    }

    /// <summary>将配置写回 GBK 编码的 config.ini（保持旧软件可读）。</summary>
    public void Save(string path, CloudConfig config)
    {
        var lines = SerializeToIniLines(config);
        File.WriteAllLines(path, lines, Gbk);
    }

    /// <summary>导出为 JSON（迁移用，UTF-8 编码）。</summary>
    public string ToJson(CloudConfig config) =>
        JsonSerializer.Serialize(config, JsonOpts);

    /// <summary>从 JSON 导入配置。</summary>
    public CloudConfig FromJson(string json) =>
        JsonSerializer.Deserialize<CloudConfig>(json, JsonOpts)
            ?? throw new FormatException("JSON 反序列化结果为 null");

    // ── INI 解析 ──────────────────────────────────────────────

    /// <summary>解析 INI 行为 {section → {key → value}}，键名统一小写以便查找。</summary>
    private static Dictionary<string, Dictionary<string, string>> ParseIni(string[] lines)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var currentSection = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                currentSection = line[1..^1].Trim();
                if (!result.ContainsKey(currentSection))
                    result[currentSection] = new(StringComparer.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0 || currentSection.Length == 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();   // 旧软件值常有前导空格，如 " tcp://..."
            result[currentSection][key] = value;   // 重复键后值覆盖（PriceListMargin 出现两次）
        }

        return result;
    }

    // ── 领域映射 ──────────────────────────────────────────────

    private static CloudConfig MapToConfig(Dictionary<string, Dictionary<string, string>> ini) =>
        new()
        {
            Window = MapWindow(ini.GetValueOrDefault("Window") ?? new(StringComparer.OrdinalIgnoreCase)),
            Order = MapOrder(ini.GetValueOrDefault("Order") ?? new(StringComparer.OrdinalIgnoreCase)),
            User = MapUser(ini.GetValueOrDefault("User") ?? new(StringComparer.OrdinalIgnoreCase)),
            Shortcuts = MapShortcuts(ini.GetValueOrDefault("Shortcuts") ?? new(StringComparer.OrdinalIgnoreCase))
        };

    private static WindowConfig MapWindow(IReadOnlyDictionary<string, string> s) => new()
    {
        MainFont = s.Get("MainFont", "新宋体"),
        CompactSpacing = s.GetInt("CompactSpacing", 7),
        FontSizeOffset = s.GetInt("FontSizeOffset", 0),
        PriceListMargin = s.GetInt("PriceListMargin", 5),
        DecTitle = s.GetInt("DecTitle", 30),
        Align = s.GetInt("Align", 1),
        NarrowReduceLength = s.GetInt("narrowReduceLength", 40),
        MouseWheelSpeed = s.GetInt("MouseWheelSpeed", 3),
        AutoSize = s.GetBool("AutoSize"),
        TickRowHeights = s.GetInt("TickRowHeights", 12),
        InstrumentWindowHeights = s.GetInt("InstrumentWindowHeights", 1000),
        PriceListRatios = s.GetIntArray("PriceListRatios", [10, 25, 30, 25, 10])
    };

    private static OrderConfig MapOrder(IReadOnlyDictionary<string, string> s) => new()
    {
        Spck = s.GetBool("SPCK"),
        Gzck = s.GetBool("GZCK"),
        RiskOpen = s.GetBool("RiskOpen"),
        MaxCancelGz = s.GetInt("MaxCancelGZ", 395),
        MaxCancelSp = s.GetInt("MaxCancelSP", 10000),
        MaxCancelQq = s.GetInt("MaxCancelQQ", 10000),
        MaxInputCount = s.GetInt("MaxInputCount", 0),
        MaxPositionCount = s.GetInt("MaxPositionCount", 0)
    };

    private static UserConfig MapUser(IReadOnlyDictionary<string, string> s) => new()
    {
        HqAddress = s.Get("HQAddress", "tcp://140.207.230.97:61213"),
        Qdp = s.GetInt("QDP", 0),
        RunMode = s.GetInt("RunMode", 0),
        CloudRiskOn = s.GetBool("CloudRiskOn"),
        HqffOn = s.GetBool("HQFFON"),
        HqffIp = s.Get("HQFFIP", "127.0.0.1"),
        HqffPort = s.GetInt("HQFFPORT", 56789),
        MOrderXSpeed = s.GetInt("MOrderXSpeed", 200),
        MOrderXStop = s.GetInt("MOrderXStop", 2200),
        Pw = s.Get("PW", string.Empty),
        MOrderTimes = ParseMOrderTimes(s)
    };

    private static ShortcutConfig MapShortcuts(IReadOnlyDictionary<string, string> s) => new()
    {
        SelectiveCancelAll = s.Get("SelectiveCancelAll", "Space"),
        ForceCancelAll = s.Get("ForceCancelAll", "W"),
        RecenterAsk = s.Get("RecenterAsk", "A"),
        RecenterBid = s.Get("RecenterBid", "D"),
        ToggleOnlyOpen = s.Get("ToggleOnlyOpen", "F"),
        MoveSelectionUp = s.Get("MoveSelectionUp", "Up"),
        MoveSelectionDown = s.Get("MoveSelectionDown", "Down")
    };

    /// <summary>从 MOrderTime1..MOrderTime9 提取 9 个时间点。</summary>
    private static IReadOnlyList<TimeOnly> ParseMOrderTimes(IReadOnlyDictionary<string, string> s)
    {
        var times = new TimeOnly[9];
        for (var i = 0; i < 9; i++)
        {
            var raw = s.Get($"MOrderTime{i + 1}", string.Empty);
            times[i] = TimeOnly.TryParseExact(raw, "HH:mm:ss", CultureInfo.InvariantCulture,
                                               DateTimeStyles.None, out var t)
                ? t
                : new TimeOnly(0, 0);
        }
        return times;
    }

    // ── INI 序列化 ────────────────────────────────────────────

    private static IEnumerable<string> SerializeToIniLines(CloudConfig c)
    {
        yield return "[Window]";
        yield return $"MainFont={c.Window.MainFont}";
        yield return $"CompactSpacing={c.Window.CompactSpacing}";
        yield return $"FontSizeOffset={c.Window.FontSizeOffset}";
        yield return $"PriceListMargin={c.Window.PriceListMargin}";
        yield return $"DecTitle={c.Window.DecTitle}";
        yield return $"Align={c.Window.Align}";
        yield return $"narrowReduceLength={c.Window.NarrowReduceLength}";
        yield return $"MouseWheelSpeed={c.Window.MouseWheelSpeed}";
        yield return $"AutoSize={(c.Window.AutoSize ? 1 : 0)}";
        yield return $"TickRowHeights={c.Window.TickRowHeights}";
        yield return $"InstrumentWindowHeights={c.Window.InstrumentWindowHeights}";
        yield return $"PriceListRatios={string.Join(',', c.Window.PriceListRatios)}";
        yield return $"PriceListMargin={c.Window.PriceListMargin}";

        yield return "[Order]";
        yield return $"SPCK={(c.Order.Spck ? 1 : 0)}";
        yield return $"GZCK={(c.Order.Gzck ? 1 : 0)}";
        yield return $"RiskOpen={(c.Order.RiskOpen ? 1 : 0)}";
        yield return $"MaxCancelGZ={c.Order.MaxCancelGz}";
        yield return $"MaxCancelSP={c.Order.MaxCancelSp}";
        yield return $"MaxCancelQQ={c.Order.MaxCancelQq}";
        yield return $"MaxInputCount={c.Order.MaxInputCount}";
        yield return $"MaxPositionCount={c.Order.MaxPositionCount}";

        yield return "[User]";
        yield return $"HQAddress= {c.User.HqAddress}";
        yield return $"QDP={c.User.Qdp}";
        yield return $"RunMode={c.User.RunMode}";
        yield return $"CloudRiskOn={(c.User.CloudRiskOn ? 1 : 0)}";
        yield return $"HQFFON={(c.User.HqffOn ? 1 : 0)}";
        yield return $"HQFFIP={c.User.HqffIp}";
        yield return $"HQFFPORT={c.User.HqffPort}";
        yield return $"MOrderXSpeed={c.User.MOrderXSpeed}";
        yield return $"MOrderXStop={c.User.MOrderXStop}";
        yield return $"PW={c.User.Pw}";
        for (var i = 0; i < c.User.MOrderTimes.Count; i++)
            yield return $"MOrderTime{i + 1}={c.User.MOrderTimes[i]:HH:mm:ss}";

        yield return "[Shortcuts]";
        yield return $"SelectiveCancelAll={c.Shortcuts.SelectiveCancelAll}";
        yield return $"ForceCancelAll={c.Shortcuts.ForceCancelAll}";
        yield return $"RecenterAsk={c.Shortcuts.RecenterAsk}";
        yield return $"RecenterBid={c.Shortcuts.RecenterBid}";
        yield return $"ToggleOnlyOpen={c.Shortcuts.ToggleOnlyOpen}";
        yield return $"MoveSelectionUp={c.Shortcuts.MoveSelectionUp}";
        yield return $"MoveSelectionDown={c.Shortcuts.MoveSelectionDown}";
    }
}

/// <summary>INI 字典扩展：类型安全取值，缺失时返回默认值。</summary>
internal static class IniDictExtensions
{
    public static string Get(this IReadOnlyDictionary<string, string> d, string key, string def) =>
        d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

    public static int GetInt(this IReadOnlyDictionary<string, string> d, string key, int def) =>
        int.TryParse(d.GetValueOrDefault(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

    public static bool GetBool(this IReadOnlyDictionary<string, string> d, string key) =>
        d.GetInt(key, 0) != 0;

    public static IReadOnlyList<int> GetIntArray(this IReadOnlyDictionary<string, string> d, string key, int[] def) =>
        d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .Select(s => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
                  .ToArray()
            : def;
}
