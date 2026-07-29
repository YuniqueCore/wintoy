using System.ComponentModel;
using System.Text.Json;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace FuturesTrader.Mcp;

/// <summary>
/// MCP 工具集：暴露 config.ini 的读写能力给外部 agent。
/// 工具方法通过 DI 注入 <see cref="IConfigRepository"/> 与 <see cref="ConfigFileOptions"/>，
/// 复用现有 GBK INI 读写逻辑，以 config.ini 为单一真相源。
/// 局部更新工具先 Load 全量 → with 替换某段 → Save，保证其余段不丢失。
/// </summary>
[McpServerToolType]
public static class ConfigTools
{
    /// <summary>JSON 序列化选项：缩进 + 中文可读（非 \u 转义），与 ConfigRepository.ToJson 一致。</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(
            System.Text.Unicode.UnicodeRanges.All)
    };

    /// <summary>加载并返回完整配置（Window + Order + User 三段）。</summary>
    [McpServerTool, Description("获取完整的期货软件配置（Window/Order/User 三段，JSON 格式）。")]
    public static string GetConfig(
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options)
    {
        var config = repo.Load(options.Value.Path);
        return JsonSerializer.Serialize(config, JsonOpts);
    }

    /// <summary>局部更新 Window 段并保存，其余段保持原值。返回更新后的完整配置。</summary>
    [McpServerTool, Description("局部更新 Window 段（窗口外观/交互参数）并保存。传入完整 Window 段 JSON，其余段保持不变。返回更新后的完整配置。")]
    public static string UpdateWindowConfig(
        [Description("Window 段配置 JSON：MainFont/CompactSpacing/FontSizeOffset/PriceListMargin/DecTitle/Align/NarrowReduceLength/MouseWheelSpeed/AutoSize/TickRowHeights/InstrumentWindowHeights/PriceListRatios")]
        WindowConfig window,
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options)
    {
        var current = repo.Load(options.Value.Path);
        var updated = current with { Window = window };
        repo.Save(options.Value.Path, updated);
        return JsonSerializer.Serialize(updated, JsonOpts);
    }

    /// <summary>局部更新 Order 段并保存，其余段保持原值。返回更新后的完整配置。</summary>
    [McpServerTool, Description("局部更新 Order 段（交易风控参数）并保存。传入完整 Order 段 JSON，其余段保持不变。返回更新后的完整配置。")]
    public static string UpdateOrderConfig(
        [Description("Order 段配置 JSON：Spck/Gzck/RiskOpen/MaxCancelGz/MaxCancelSp/MaxCancelQq/MaxInputCount/MaxPositionCount")]
        OrderConfig order,
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options)
    {
        var current = repo.Load(options.Value.Path);
        var updated = current with { Order = order };
        repo.Save(options.Value.Path, updated);
        return JsonSerializer.Serialize(updated, JsonOpts);
    }

    /// <summary>局部更新 User 段并保存，其余段保持原值。返回更新后的完整配置。</summary>
    [McpServerTool, Description("局部更新 User 段（行情/交易连接与开盘抢单参数）并保存。传入完整 User 段 JSON，其余段保持不变。返回更新后的完整配置。")]
    public static string UpdateUserConfig(
        [Description("User 段配置 JSON：HqAddress/Qdp/RunMode/CloudRiskOn/HqffOn/HqffIp/HqffPort/MOrderXSpeed/MOrderXStop/Pw/MOrderTimes(9个TimeOnly)")]
        UserConfig user,
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options)
    {
        var current = repo.Load(options.Value.Path);
        var updated = current with { User = user };
        repo.Save(options.Value.Path, updated);
        return JsonSerializer.Serialize(updated, JsonOpts);
    }

    /// <summary>全量保存配置（覆盖三段）。传入完整 CloudConfig JSON。返回保存结果。</summary>
    [McpServerTool, Description("全量保存配置（覆盖 Window/Order/User 三段）。传入完整 CloudConfig JSON。返回保存确认。")]
    public static string SaveConfig(
        [Description("完整配置 JSON，含 Window/Order/User 三段")]
        CloudConfig config,
        IConfigRepository repo,
        IOptions<ConfigFileOptions> options)
    {
        repo.Save(options.Value.Path, config);
        return $"已保存到 {options.Value.Path}（GBK 编码，旧软件可读）";
    }
}
