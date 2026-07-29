using System.Text.Json;
using FluentAssertions;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Mcp;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Mcp.Tests;

/// <summary>
/// ConfigTools 单元测试：覆盖 5 个 MCP 工具的核心契约。
/// 用内存 Stub 仓库隔离 INI 持久化，聚焦工具逻辑：局部更新不丢段、JSON 序列化、保存转发。
/// </summary>
public class ConfigToolsTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static IOptions<ConfigFileOptions> Options(string path = "test.ini") =>
        Microsoft.Extensions.Options.Options.Create(new ConfigFileOptions { Path = path });

    // ── get_config ────────────────────────────────────────────

    [Fact]
    public void GetConfig_returns_json_with_all_three_segments()
    {
        var repo = new StubConfigRepository(new CloudConfig
        {
            Window = new WindowConfig { MainFont = "黑体", MouseWheelSpeed = 7 },
            Order = new OrderConfig { RiskOpen = true, MaxCancelGz = 500 },
            User = new UserConfig { HqAddress = "tcp://1.2.3.4:5678", MOrderXSpeed = 300 }
        });

        var json = ConfigTools.GetConfig(repo, Options());

        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("Window").GetProperty("MainFont").GetString().Should().Be("黑体");
        doc.RootElement.GetProperty("Order").GetProperty("MaxCancelGz").GetInt32().Should().Be(500);
        doc.RootElement.GetProperty("User").GetProperty("HqAddress").GetString().Should().Be("tcp://1.2.3.4:5678");
    }

    [Fact]
    public void GetConfig_propagates_load_exception()
    {
        var repo = new StubConfigRepository(throwOnLoad: true);
        var act = () => ConfigTools.GetConfig(repo, Options());
        act.Should().Throw<FileNotFoundException>();
    }

    // ── update_window_config ─────────────────────────────────

    [Fact]
    public void UpdateWindowConfig_replaces_only_window_and_preserves_other_segments()
    {
        var original = new CloudConfig
        {
            Window = new WindowConfig { MainFont = "旧字体", MouseWheelSpeed = 1 },
            Order = new OrderConfig { RiskOpen = false, MaxCancelGz = 395 },
            User = new UserConfig { HqAddress = "tcp://keep-me:1234", MOrderXSpeed = 200 }
        };
        var repo = new StubConfigRepository(original);

        var newWindow = new WindowConfig { MainFont = "新宋体", MouseWheelSpeed = 9, AutoSize = true };
        var json = ConfigTools.UpdateWindowConfig(newWindow, repo, Options());

        // 返回的完整配置中 Window 已替换，Order/User 保持原值
        var updated = JsonSerializer.Deserialize<CloudConfig>(json, JsonOpts)!;
        updated.Window.MainFont.Should().Be("新宋体");
        updated.Window.MouseWheelSpeed.Should().Be(9);
        updated.Window.AutoSize.Should().BeTrue();
        updated.Order.RiskOpen.Should().BeFalse("Order 段不应被改动");
        updated.Order.MaxCancelGz.Should().Be(395);
        updated.User.HqAddress.Should().Be("tcp://keep-me:1234", "User 段不应被改动");
        updated.User.MOrderXSpeed.Should().Be(200);

        // 仓库已收到保存调用，且保存的是替换后的完整配置
        repo.LastSaved.Should().NotBeNull();
        repo.LastSaved!.Window.MainFont.Should().Be("新宋体");
        repo.LastSaved.Order.MaxCancelGz.Should().Be(395);
    }

    // ── update_order_config ──────────────────────────────────

    [Fact]
    public void UpdateOrderConfig_replaces_only_order_and_preserves_other_segments()
    {
        var original = new CloudConfig
        {
            Window = new WindowConfig { MainFont = "保留字体", TickRowHeights = 14 },
            Order = new OrderConfig { RiskOpen = false, MaxCancelSp = 10000 },
            User = new UserConfig { HqAddress = "tcp://keep:1", MOrderXStop = 2200 }
        };
        var repo = new StubConfigRepository(original);

        var newOrder = new OrderConfig { RiskOpen = true, MaxCancelGz = 999, MaxCancelSp = 5000 };
        var json = ConfigTools.UpdateOrderConfig(newOrder, repo, Options());

        var updated = JsonSerializer.Deserialize<CloudConfig>(json, JsonOpts)!;
        updated.Order.RiskOpen.Should().BeTrue();
        updated.Order.MaxCancelGz.Should().Be(999);
        updated.Order.MaxCancelSp.Should().Be(5000);
        updated.Window.MainFont.Should().Be("保留字体", "Window 段不应被改动");
        updated.Window.TickRowHeights.Should().Be(14);
        updated.User.MOrderXStop.Should().Be(2200, "User 段不应被改动");
    }

    // ── update_user_config ───────────────────────────────────

    [Fact]
    public void UpdateUserConfig_replaces_only_user_and_preserves_other_segments()
    {
        var original = new CloudConfig
        {
            Window = new WindowConfig { MainFont = "keep-font", Align = 1 },
            Order = new OrderConfig { RiskOpen = true, MaxCancelQq = 10000 },
            User = new UserConfig { HqAddress = "tcp://old:1", MOrderXSpeed = 200 }
        };
        var repo = new StubConfigRepository(original);

        var newUser = new UserConfig
        {
            HqAddress = "tcp://new:9999",
            MOrderXSpeed = 500,
            MOrderXStop = 3000,
            CloudRiskOn = true,
            MOrderTimes =
            [
                new(9, 0, 0), new(10, 0, 0), new(11, 0, 0),
                new(13, 0, 0), new(14, 0, 0), new(15, 0, 0),
                new(21, 0, 0), new(22, 0, 0), new(23, 0, 0)
            ]
        };
        var json = ConfigTools.UpdateUserConfig(newUser, repo, Options());

        var updated = JsonSerializer.Deserialize<CloudConfig>(json, JsonOpts)!;
        updated.User.HqAddress.Should().Be("tcp://new:9999");
        updated.User.MOrderXSpeed.Should().Be(500);
        updated.User.CloudRiskOn.Should().BeTrue();
        updated.User.MOrderTimes.Should().HaveCount(9);
        updated.Window.MainFont.Should().Be("keep-font", "Window 段不应被改动");
        updated.Order.RiskOpen.Should().BeTrue("Order 段不应被改动");
        updated.Order.MaxCancelQq.Should().Be(10000);
    }

    // ── save_config ──────────────────────────────────────────

    [Fact]
    public void SaveConfig_forwards_full_config_to_repository()
    {
        var repo = new StubConfigRepository(new CloudConfig());
        var fullConfig = new CloudConfig
        {
            Window = new WindowConfig { MainFont = "全量保存字体" },
            Order = new OrderConfig { MaxCancelGz = 777 },
            User = new UserConfig { HqAddress = "tcp://saved:1" }
        };

        var result = ConfigTools.SaveConfig(fullConfig, repo, Options("my.ini"));

        repo.LastSaved.Should().BeSameAs(fullConfig, "应原样转发完整配置到仓库 Save");
        result.Should().Contain("my.ini", "返回信息应包含路径");
        result.Should().Contain("已保存");
    }

    // ── Stub ─────────────────────────────────────────────────

    /// <summary>
    /// 内存 Stub 仓库：Load 返回 <see cref="Current"/>，Save 记录最后保存值并更新 Current。
    /// throwOnLoad=true 时 Load 抛 FileNotFoundException，用于测试异常传播。
    /// </summary>
    private sealed class StubConfigRepository : IConfigRepository
    {
        public CloudConfig Current { get; set; }
        public CloudConfig? LastSaved { get; private set; }

        private readonly bool _throwOnLoad;

        public StubConfigRepository(CloudConfig? initial = null, bool throwOnLoad = false)
        {
            Current = initial ?? new CloudConfig();
            _throwOnLoad = throwOnLoad;
        }

        public CloudConfig Load(string path)
        {
            if (_throwOnLoad) throw new FileNotFoundException($"配置文件不存在: {path}", path);
            return Current;
        }

        public void Save(string path, CloudConfig config)
        {
            Current = config;
            LastSaved = config;
        }
    }
}
