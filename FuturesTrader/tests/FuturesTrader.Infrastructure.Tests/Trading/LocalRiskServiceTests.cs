using FluentAssertions;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.Trading;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Infrastructure.Tests.Trading;

/// <summary>
/// <see cref="LocalRiskService"/> 单元测试：覆盖风控开关、报单数/持仓数/撤单数限制、品种分类。
/// 用纯 OrderConfig record 构造，无文件/网络依赖，聚焦规则逻辑。
/// </summary>
public class LocalRiskServiceTests
{
    private static OrderRequest Buy(string instrumentId, OffsetFlag offset = OffsetFlag.Open, int volume = 1) => new()
    {
        InstrumentId = instrumentId,
        Direction = Direction.Buy,
        OffsetFlag = offset,
        Price = 100m,
        Volume = volume
    };

    // ── RiskOpen 总开关 ───────────────────────────────────────

    [Fact]
    public void CheckOrder_allows_when_RiskOpen_false()
    {
        var cfg = new OrderConfig { RiskOpen = false, MaxInputCount = 1 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckOrder(Buy("ag2608"), currentOrderCount: 99, currentPositionCount: 99);

        allowed.Should().BeTrue("RiskOpen=false 时所有报单放行");
    }

    [Fact]
    public void CheckCancel_allows_when_RiskOpen_false()
    {
        var cfg = new OrderConfig { RiskOpen = false, Spck = true, MaxCancelSp = 1 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckCancel("ag2608", svc.CurrentCounters);

        allowed.Should().BeTrue("RiskOpen=false 时撤单不限制");
    }

    // ── 报单数限制 ──────────────────────────────────────────

    [Fact]
    public void CheckOrder_rejects_when_MaxInputCount_reached()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxInputCount = 3 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, reason) = svc.CheckOrder(Buy("ag2608"), currentOrderCount: 3, currentPositionCount: 0);

        allowed.Should().BeFalse("达到 MaxInputCount 应拒绝");
        reason.Should().Contain("3");
    }

    [Fact]
    public void CheckOrder_allows_when_below_MaxInputCount()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxInputCount = 3 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckOrder(Buy("ag2608"), currentOrderCount: 2, currentPositionCount: 0);

        allowed.Should().BeTrue("未达 MaxInputCount 应放行");
    }

    [Fact]
    public void CheckOrder_MaxInputCount_zero_means_unlimited()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxInputCount = 0 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckOrder(Buy("ag2608"), currentOrderCount: 99999, currentPositionCount: 0);

        allowed.Should().BeTrue("MaxInputCount=0 表示不限制");
    }

    // ── 持仓数限制 ──────────────────────────────────────────

    [Fact]
    public void CheckOrder_rejects_open_when_MaxPositionCount_reached()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxPositionCount = 2 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, reason) = svc.CheckOrder(
            Buy("ag2608", OffsetFlag.Open), currentOrderCount: 0, currentPositionCount: 2);

        allowed.Should().BeFalse("开仓达 MaxPositionCount 应拒绝");
        reason.Should().Contain("2");
    }

    [Fact]
    public void CheckOrder_allows_close_even_at_MaxPositionCount()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxPositionCount = 2 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckOrder(
            Buy("ag2608", OffsetFlag.Close), currentOrderCount: 0, currentPositionCount: 2);

        allowed.Should().BeTrue("平仓不增加持仓，应放行");
    }

    // ── 数量校验 ────────────────────────────────────────────

    [Fact]
    public void CheckOrder_rejects_zero_volume()
    {
        var cfg = new OrderConfig { RiskOpen = true };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        var (allowed, _) = svc.CheckOrder(Buy("ag2608", volume: 0), currentOrderCount: 0, currentPositionCount: 0);

        allowed.Should().BeFalse("数量必须 > 0");
    }

    // ── 撤单数限制：品种分类 ─────────────────────────────────

    [Fact]
    public void CheckCancel_rejects_SP_when_Spck_on_and_limit_reached()
    {
        var cfg = new OrderConfig { RiskOpen = true, Spck = true, MaxCancelSp = 5 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);
        for (var i = 0; i < 5; i++) svc.RecordCancel("ag2608"); // 商品 ag2608 → SP

        var (allowed, reason) = svc.CheckCancel("ag2608", svc.CurrentCounters);

        allowed.Should().BeFalse("商品撤单达上限应拒绝");
        reason.Should().Contain("5").And.Contain("SP");
    }

    [Fact]
    public void CheckCancel_allows_SP_when_Spck_off()
    {
        var cfg = new OrderConfig { RiskOpen = true, Spck = false, MaxCancelSp = 1 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);
        svc.RecordCancel("ag2608");

        var (allowed, _) = svc.CheckCancel("ag2608", svc.CurrentCounters);

        allowed.Should().BeTrue("Spck=false 时商品撤单不限制");
    }

    [Fact]
    public void CheckCancel_rejects_GZ_when_Gzck_on_and_limit_reached()
    {
        var cfg = new OrderConfig { RiskOpen = true, Gzck = true, MaxCancelGz = 3 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);
        for (var i = 0; i < 3; i++) svc.RecordCancel("IF2608"); // IF2608 → GZ

        var (allowed, reason) = svc.CheckCancel("IF2608", svc.CurrentCounters);

        allowed.Should().BeFalse("股指撤单达上限应拒绝");
        reason.Should().Contain("3").And.Contain("GZ");
    }

    [Fact]
    public void CheckCancel_classifies_IF_IH_IC_IM_as_GZ()
    {
        var cfg = new OrderConfig { RiskOpen = true, Gzck = true, MaxCancelGz = 1 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        // 每个 GZ 合约撤一次，所有 GZ 撤单应合并计数
        svc.RecordCancel("IF2608");
        svc.RecordCancel("IH2608");
        svc.RecordCancel("IC2608");
        svc.RecordCancel("IM2608");

        var (allowed, reason) = svc.CheckCancel("IF2608", svc.CurrentCounters);

        allowed.Should().BeFalse("IF/IH/IC/IM 都属于 GZ，计数应合并");
        reason.Should().NotBeNull();
    }

    [Fact]
    public void CheckCancel_classifies_option_as_QQ()
    {
        var cfg = new OrderConfig { RiskOpen = true, MaxCancelQq = 1 };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);
        svc.RecordCancel("IO2609-C-4000");

        var (allowed, reason) = svc.CheckCancel("IO2609-P-4000", svc.CurrentCounters);

        allowed.Should().BeFalse("含 -C-/-P- 的合约为期权，撤单应受 QQ 限制");
        reason.Should().Contain("QQ");
    }

    // ── 计数器与重置 ────────────────────────────────────────

    [Fact]
    public void RecordCancel_increments_correct_category()
    {
        var cfg = new OrderConfig { RiskOpen = true };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);

        svc.RecordCancel("ag2608");   // SP
        svc.RecordCancel("IF2608");   // GZ
        svc.RecordCancel("IO2609-C-4000"); // QQ

        svc.CurrentCounters.Should().BeEquivalentTo(new RiskCancelCounters
        {
            GzCount = 1,
            SpCount = 1,
            QqCount = 1
        });
    }

    [Fact]
    public void Reset_clears_all_counters()
    {
        var cfg = new OrderConfig { RiskOpen = true };
        var svc = new LocalRiskService(cfg, NullLogger<LocalRiskService>.Instance);
        svc.RecordCancel("ag2608");
        svc.RecordCancel("IF2608");

        svc.Reset();

        svc.CurrentCounters.Should().BeEquivalentTo(new RiskCancelCounters
        {
            GzCount = 0,
            SpCount = 0,
            QqCount = 0
        });
    }
}
