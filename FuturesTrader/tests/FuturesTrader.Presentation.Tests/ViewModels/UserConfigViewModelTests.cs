using FluentAssertions;
using FuturesTrader.Domain.Configuration;
using FuturesTrader.Presentation.ViewModels;

namespace FuturesTrader.Presentation.Tests.ViewModels;

/// <summary>
/// <see cref="UserConfigViewModel"/> 单元测试：覆盖 Hydrate/ToConfig 双向映射和 MOrderTimes CRUD。
/// 纯 VM 测试，不依赖 IConfigRepository；MOrderTimes 校验在 VM 层完成（HH:mm:ss + 去重 + 不超 9）。
/// </summary>
public class UserConfigViewModelTests
{
    private static UserConfigViewModel CreateVm() => new();

    // ── Hydrate ──────────────────────────────────────────────────

    [Fact]
    public void Hydrate_populates_simple_fields_from_record()
    {
        var vm = CreateVm();
        var u = new UserConfig
        {
            HqAddress = "tcp://1.2.3.4:9999",
            Qdp = 7,
            RunMode = 2,
            CloudRiskOn = true,
            HqffOn = true,
            HqffIp = "10.0.0.1",
            HqffPort = 12345,
            MOrderXSpeed = 500,
            MOrderXStop = 3000,
        };

        vm.Hydrate(u);

        vm.HqAddress.Should().Be("tcp://1.2.3.4:9999");
        vm.Qdp.Should().Be(7);
        vm.RunMode.Should().Be(2);
        vm.CloudRiskOn.Should().BeTrue();
        vm.HqffOn.Should().BeTrue();
        vm.HqffIp.Should().Be("10.0.0.1");
        vm.HqffPort.Should().Be(12345);
        vm.MOrderXSpeed.Should().Be(500);
        vm.MOrderXStop.Should().Be(3000);
    }

    [Fact]
    public void Hydrate_formats_MOrderTimes_to_HH_mm_ss_strings()
    {
        var vm = CreateVm();
        var u = new UserConfig
        {
            MOrderTimes = [new TimeOnly(9, 29, 58), new TimeOnly(20, 59, 58), new TimeOnly(10, 31, 0)]
        };

        vm.Hydrate(u);

        vm.MOrderTimes.Should().Equal("09:29:58", "20:59:58", "10:31:00");
    }

    [Fact]
    public void Hydrate_clears_previous_MOrderTimes()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig { MOrderTimes = [new TimeOnly(9, 0, 0)] });
        vm.Hydrate(new UserConfig { MOrderTimes = [new TimeOnly(10, 0, 0), new TimeOnly(11, 0, 0)] });

        vm.MOrderTimes.Should().Equal("10:00:00", "11:00:00");
    }

    // ── ToConfig ─────────────────────────────────────────────────

    [Fact]
    public void ToConfig_preserves_all_simple_fields()
    {
        var vm = CreateVm();
        var original = new UserConfig
        {
            HqAddress = "tcp://old:1",
            Qdp = 1,
            RunMode = 0,
            MOrderXSpeed = 200,
            MOrderXStop = 2200,
            MOrderTimes = [new TimeOnly(9, 29, 58)],
        };
        vm.Hydrate(original);
        vm.HqAddress = "tcp://new:9999";
        vm.MOrderXSpeed = 600;

        var updated = vm.ToConfig(original);

        updated.HqAddress.Should().Be("tcp://new:9999");
        updated.MOrderXSpeed.Should().Be(600);
        updated.MOrderXStop.Should().Be(2200);
    }

    [Fact]
    public void ToConfig_converts_MOrderTimes_back_to_TimeOnly()
    {
        var vm = CreateVm();
        var original = new UserConfig
        {
            MOrderTimes = [new TimeOnly(9, 29, 58), new TimeOnly(8, 59, 58)]
        };
        vm.Hydrate(original);
        vm.NewMOrderTime = "13:29:58";
        vm.AddMOrderTimeCommand.Execute(null);
        vm.RemoveMOrderTimeCommand.Execute("08:59:58");

        var updated = vm.ToConfig(original);

        updated.MOrderTimes.Should().Equal(
            new TimeOnly(9, 29, 58),
            new TimeOnly(13, 29, 58));
    }

    [Fact]
    public void ToConfig_preserves_Pw_field_not_exposed_in_vm()
    {
        var vm = CreateVm();
        var original = new UserConfig { Pw = "secret-pw" };

        var updated = vm.ToConfig(original);

        updated.Pw.Should().Be("secret-pw", "Pw 字段未在 VM 暴露，ToConfig 必须保留原值");
    }

    // ── MOrderTimes CRUD: Add ────────────────────────────────────

    [Fact]
    public void AddMOrderTime_appends_when_format_valid_and_not_full()
    {
        var vm = CreateVm();
        // 故意用空集合，确保未满
        vm.Hydrate(new UserConfig { MOrderTimes = [] });
        vm.NewMOrderTime = "14:00:00";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimes.Should().Contain("14:00:00");
        vm.MOrderTimeError.Should().BeNull();
        vm.NewMOrderTime.Should().BeEmpty("成功后清空输入框");
    }

    [Fact]
    public void AddMOrderTime_rejects_empty_input()
    {
        var vm = CreateVm();
        vm.NewMOrderTime = "  ";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimeError.Should().NotBeNull().And.Contain("不能为空");
    }

    [Fact]
    public void AddMOrderTime_rejects_invalid_format()
    {
        var vm = CreateVm();
        vm.NewMOrderTime = "9-30-00";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimeError.Should().NotBeNull().And.Contain("格式错误");
    }

    [Fact]
    public void AddMOrderTime_rejects_duplicate()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig()); // 含 09:29:58
        vm.NewMOrderTime = "09:29:58";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimeError.Should().NotBeNull().And.Contain("重复");
    }

    [Fact]
    public void AddMOrderTime_rejects_when_full()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig
        {
            MOrderTimes =
            [
                new(9, 0, 0), new(10, 0, 0), new(11, 0, 0),
                new(13, 0, 0), new(14, 0, 0), new(15, 0, 0),
                new(21, 0, 0), new(22, 0, 0), new(23, 0, 0)
            ]
        });
        vm.NewMOrderTime = "12:00:00";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimeError.Should().NotBeNull().And.Contain("上限");
    }

    [Fact]
    public void AddMOrderTime_normalizes_to_HH_mm_ss()
    {
        var vm = CreateVm();
        vm.NewMOrderTime = "9:5:7"; // 单位数小时/分/秒

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimes.Should().Contain("09:05:07");
    }

    [Fact]
    public void AddMOrderTime_disabled_when_input_blank()
    {
        var vm = CreateVm();
        vm.NewMOrderTime = "";

        vm.AddMOrderTimeCommand.CanExecute(null).Should().BeFalse("空输入时 Add 不可执行");
    }

    // ── MOrderTimes CRUD: Remove ─────────────────────────────────

    [Fact]
    public void RemoveMOrderTime_removes_existing_time()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig()); // 默认 9 个，含 09:29:58

        vm.RemoveMOrderTimeCommand.Execute("09:29:58");

        vm.MOrderTimes.Should().NotContain("09:29:58");
        vm.MOrderTimeError.Should().BeNull();
    }

    [Fact]
    public void RemoveMOrderTime_is_noop_for_missing_time()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig());

        vm.RemoveMOrderTimeCommand.Execute("99:99:99");

        vm.MOrderTimes.Should().HaveCount(9, "不存在的项 Remove 不应改变集合");
    }

    [Fact]
    public void RemoveMOrderTime_allows_adding_new_after_removal()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig
        {
            MOrderTimes =
            [
                new(9, 0, 0), new(10, 0, 0), new(11, 0, 0),
                new(13, 0, 0), new(14, 0, 0), new(15, 0, 0),
                new(21, 0, 0), new(22, 0, 0), new(23, 0, 0)
            ]
        });
        vm.RemoveMOrderTimeCommand.Execute("10:00:00");
        vm.NewMOrderTime = "10:30:00";

        vm.AddMOrderTimeCommand.Execute(null);

        vm.MOrderTimes.Should().HaveCount(9).And.Contain("10:30:00");
    }

    // ── NewMOrderTime 联动 ──────────────────────────────────────

    [Fact]
    public void NewMOrderTime_change_clears_error()
    {
        var vm = CreateVm();
        vm.NewMOrderTime = "bad";
        vm.AddMOrderTimeCommand.Execute(null);
        vm.MOrderTimeError.Should().NotBeNull();

        vm.NewMOrderTime = "12:00:00";

        vm.MOrderTimeError.Should().BeNull("输入变化时清掉错误提示");
    }

    [Fact]
    public void IsMOrderTimesFull_reflects_count()
    {
        var vm = CreateVm();
        vm.Hydrate(new UserConfig
        {
            MOrderTimes =
            [
                new(9, 0, 0), new(10, 0, 0), new(11, 0, 0),
                new(13, 0, 0), new(14, 0, 0), new(15, 0, 0),
                new(21, 0, 0), new(22, 0, 0), new(23, 0, 0)
            ]
        });

        vm.IsMOrderTimesFull.Should().BeTrue();

        vm.RemoveMOrderTimeCommand.Execute("09:00:00");

        vm.IsMOrderTimesFull.Should().BeFalse();
    }
}
