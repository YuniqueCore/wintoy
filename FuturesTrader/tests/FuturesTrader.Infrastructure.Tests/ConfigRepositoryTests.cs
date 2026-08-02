using System.Text;
using FluentAssertions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Infrastructure.Persistence;

namespace FuturesTrader.Infrastructure.Tests;

/// <summary>
/// ConfigRepository 单元测试：覆盖 GBK 读写、INI 解析、领域映射、JSON 迁移、往返一致性。
/// </summary>
public class ConfigRepositoryTests : IDisposable
{
    private readonly ConfigRepository _repo = new();
    private readonly string _tempDir;

    public ConfigRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ft_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── 真实旧 config.ini 读取测试 ───────────────────────────

    [Fact]
    public void Load_real_config_ini_decodes_gbk_correctly()
    {
        // Arrange: 用 GBK 写入与旧软件完全一致的 config.ini
        var path = WriteGbkIni(SampleConfigIni);

        // Act
        var config = _repo.Load(path);

        // Assert: 验证关键字段被正确解析
        config.Window.MainFont.Should().Be("新宋体", "GBK 中文应正确解码");
        config.Window.PriceListRatios.Should().Equal([10, 25, 30, 25, 10]);
        config.Window.MouseWheelSpeed.Should().Be(3);
        config.Window.AutoSize.Should().BeFalse();

        config.Order.MaxCancelGz.Should().Be(395);
        config.Order.MaxCancelSp.Should().Be(10000);
        config.Order.RiskOpen.Should().BeFalse();

        config.User.HqAddress.Should().Be("tcp://140.207.230.97:61213", "前导空格应被 Trim");
        config.User.MOrderXSpeed.Should().Be(200);
        config.User.MOrderXStop.Should().Be(2200);
        config.User.MOrderTimes.Should().HaveCount(9);
        config.User.MOrderTimes[0].Should().Be(new TimeOnly(9, 29, 58));
        config.User.MOrderTimes[8].Should().Be(new TimeOnly(10, 31, 0));
        config.Shortcuts.Should().Be(new ShortcutConfig(), "旧配置缺失快捷键段时使用默认值");
    }

    // ── 边缘条件 ─────────────────────────────────────────────

    [Fact]
    public void Load_throws_when_file_not_found()
    {
        var act = () => _repo.Load(Path.Combine(_tempDir, "nonexistent.ini"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_handles_duplicate_keys_last_wins()
    {
        // PriceListMargin 在 [Window] 段出现两次（旧软件的真实情况）
        var ini = """
            [Window]
            PriceListMargin=5
            PriceListMargin=8
            """;
        var path = WriteGbkIni(ini);
        var config = _repo.Load(path);
        config.Window.PriceListMargin.Should().Be(8, "重复键后值应覆盖");
    }

    [Fact]
    public void Load_trims_leading_space_in_hq_address()
    {
        var ini = """
            [User]
            HQAddress= tcp://127.0.0.1:1234
            """;
        var path = WriteGbkIni(ini);
        var config = _repo.Load(path);
        config.User.HqAddress.Should().Be("tcp://127.0.0.1:1234");
    }

    [Fact]
    public void Load_parses_empty_pw_as_empty_string()
    {
        var ini = """
            [User]
            PW=
            """;
        var path = WriteGbkIni(ini);
        var config = _repo.Load(path);
        config.User.Pw.Should().BeEmpty();
    }

    [Fact]
    public void Load_uses_defaults_for_missing_sections()
    {
        var path = WriteGbkIni("[Window]\nMainFont=黑体\n");
        var config = _repo.Load(path);
        config.Window.MainFont.Should().Be("黑体");
        config.Order.MaxCancelGz.Should().Be(395, "缺失 [Order] 段应使用默认值");
        config.User.MOrderXSpeed.Should().Be(200, "缺失 [User] 段应使用默认值");
    }

    // ── 往返一致性 ───────────────────────────────────────────

    [Fact]
    public void Save_then_load_produces_equivalent_config()
    {
        var original = new CloudConfig
        {
            Window = new WindowConfig
            {
                MainFont = "微软雅黑",
                CompactSpacing = 10,
                TickRowHeights = 18,
                AskQuoteRowCount = 36,
                BidQuoteRowCount = 44,
                PriceListRatios = [15, 20, 30, 20, 15],
                AutoSize = true
            },
            Order = new OrderConfig
            {
                RiskOpen = true,
                MaxCancelGz = 500,
                MaxCancelSp = 20000
            },
            User = new UserConfig
            {
                HqAddress = "tcp://1.2.3.4:5678",
                MOrderXSpeed = 300,
                MOrderTimes =
                [
                    new TimeOnly(9, 0, 0), new TimeOnly(10, 0, 0), new TimeOnly(11, 0, 0),
                    new TimeOnly(13, 0, 0), new TimeOnly(14, 0, 0), new TimeOnly(15, 0, 0),
                    new TimeOnly(21, 0, 0), new TimeOnly(22, 0, 0), new TimeOnly(23, 0, 0)
                ]
            },
            Shortcuts = new ShortcutConfig
            {
                SelectiveCancelAll = "Ctrl+Space",
                ForceCancelAll = "Ctrl+Shift+W",
                RecenterAsk = "PageUp",
                RecenterBid = "PageDown",
                ToggleOnlyOpen = "Ctrl+F",
                MoveSelectionUp = "Ctrl+Up",
                MoveSelectionDown = "Ctrl+Down"
            }
        };

        var path = Path.Combine(_tempDir, "roundtrip.ini");
        _repo.Save(path, original);
        var loaded = _repo.Load(path);

        loaded.Window.MainFont.Should().Be(original.Window.MainFont);
        loaded.Window.CompactSpacing.Should().Be(original.Window.CompactSpacing);
        loaded.Window.TickRowHeights.Should().Be(original.Window.TickRowHeights);
        loaded.Window.AskQuoteRowCount.Should().Be(original.Window.AskQuoteRowCount);
        loaded.Window.BidQuoteRowCount.Should().Be(original.Window.BidQuoteRowCount);
        loaded.Window.PriceListRatios.Should().Equal(original.Window.PriceListRatios);
        loaded.Window.AutoSize.Should().Be(original.Window.AutoSize);

        loaded.Order.RiskOpen.Should().Be(original.Order.RiskOpen);
        loaded.Order.MaxCancelGz.Should().Be(original.Order.MaxCancelGz);
        loaded.Order.MaxCancelSp.Should().Be(original.Order.MaxCancelSp);

        loaded.User.HqAddress.Should().Be(original.User.HqAddress);
        loaded.User.MOrderXSpeed.Should().Be(original.User.MOrderXSpeed);
        loaded.User.MOrderTimes.Should().Equal(original.User.MOrderTimes);
        loaded.Shortcuts.Should().Be(original.Shortcuts);
    }

    // ── JSON 迁移 ────────────────────────────────────────────

    [Fact]
    public void ToJson_then_FromJson_preserves_config()
    {
        var original = new CloudConfig
        {
            Window = new WindowConfig { MainFont = "楷体", MouseWheelSpeed = 5 },
            Order = new OrderConfig { RiskOpen = true, MaxCancelGz = 999 },
            User = new UserConfig { MOrderXSpeed = 500 },
            Shortcuts = new ShortcutConfig { ForceCancelAll = "Ctrl+W" }
        };

        var json = _repo.ToJson(original);
        var restored = _repo.FromJson(json);

        restored.Window.MainFont.Should().Be("楷体");
        restored.Window.MouseWheelSpeed.Should().Be(5);
        restored.Order.RiskOpen.Should().BeTrue();
        restored.Order.MaxCancelGz.Should().Be(999);
        restored.User.MOrderXSpeed.Should().Be(500);
        restored.Shortcuts.ForceCancelAll.Should().Be("Ctrl+W");
    }

    [Fact]
    public void ToJson_produces_utf8_readable_chinese()
    {
        var config = new CloudConfig { Window = new WindowConfig { MainFont = "新宋体" } };
        var json = _repo.ToJson(config);
        json.Should().Contain("新宋体", "JSON 应保留中文可读性（非 \\u 转义）");
    }

    // ── 辅助 ─────────────────────────────────────────────────

    private string WriteGbkIni(string content)
    {
        var path = Path.Combine(_tempDir, "test.ini");
        File.WriteAllText(path, content, Encoding.GetEncoding(936));
        return path;
    }

    /// <summary>与旧软件 qihuo-software/config.ini 完全一致的内容（含 GBK 中文、前导空格、重复键）。</summary>
    private const string SampleConfigIni = """
        [Window]
        MainFont=新宋体
        CompactSpacing=7
        FontSizeOffset=0
        PriceListMargin=5
        DecTitle=30
        Align=1
        narrowReduceLength=40
        MouseWheelSpeed=3
        AutoSize=0
        TickRowHeights=12
        InstrumentWindowHeights=1000
        PriceListRatios=10,25,30,25,10
        PriceListMargin=5
        [Order]
        SPCK=0
        GZCK=0
        RiskOpen=0
        MaxCancelGZ=395
        MaxCancelSP=10000
        MaxCancelQQ=10000
        MaxInputCount=0
        MaxPositionCount=0
        [User]
        HQAddress= tcp://140.207.230.97:61213
        QDP=0
        RunMode=0
        CloudRiskOn=0
        HQFFON=0
        HQFFIP=127.0.0.1
        HQFFPORT=56789
        MOrderXSpeed=200
        MOrderXStop=2200
        PW=
        MOrderTime1=09:29:58
        MOrderTime2=08:59:58
        MOrderTime3=08:54:58
        MOrderTime4=12:59:58
        MOrderTime5=20:59:58
        MOrderTime6=13:29:58
        MOrderTime7=20:54:58
        MOrderTime8=09:24:58
        MOrderTime9=10:31:00
        """;
}
