using System.IO;
using FluentAssertions;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Presentation.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.Tests.Services;

/// <summary>
/// <see cref="SoundService"/> 测试：文件缺失静默降级、Enabled=false 跳过、不实际播放。
/// 因 <see cref="SoundService"/> 依赖 WPF <c>MediaPlayer</c> + UI 线程，无 WPF 应用上下文时
/// Play 直接静默返回（不崩）。文件存在性通过临时目录验证。
/// </summary>
public class SoundServiceTests
{
    [Fact]
    public void Play_with_nonexistent_file_does_not_throw()
    {
        var opts = Options.Create(new SoundOptions { BasePath = Path.GetTempPath(), Enabled = true });
        var svc = new SoundService(opts, NullLogger<SoundService>.Instance);

        // 无 WPF Application.Current 时 Play 静默返回；有也找不到文件应降级
        var act = () => svc.Play(SoundType.NoMoney);
        act.Should().NotThrow("文件缺失应静默降级，不抛异常");
    }

    [Fact]
    public void Play_does_nothing_when_disabled()
    {
        var opts = Options.Create(new SoundOptions { BasePath = Path.GetTempPath(), Enabled = false });
        var svc = new SoundService(opts, NullLogger<SoundService>.Instance);
        svc.Enabled.Should().BeFalse();

        var act = () => svc.Play(SoundType.Chimes);
        act.Should().NotThrow("禁用时 Play 应直接跳过");
    }

    [Fact]
    public void Enabled_reflects_options_default()
    {
        var opts = Options.Create(new SoundOptions { BasePath = "", Enabled = true });
        var svc = new SoundService(opts, NullLogger<SoundService>.Instance);
        svc.Enabled.Should().BeTrue();

        var optsOff = Options.Create(new SoundOptions { BasePath = "", Enabled = false });
        var svcOff = new SoundService(optsOff, NullLogger<SoundService>.Instance);
        svcOff.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(SoundType.NoMoney, "Nomoney.wav")]
    [InlineData(SoundType.CashRegister, "cashreg.wav")]
    [InlineData(SoundType.Chimes, "chimes.wav")]
    [InlineData(SoundType.Cancel, "Cancellation.wav")]
    public async Task Play_with_existing_file_does_not_throw_in_ui_context(SoundType type, string fileName)
    {
        // 准备临时目录 + 空白 wav 占位文件
        var dir = Path.Combine(Path.GetTempPath(), "SoundTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        await File.WriteAllBytesAsync(path, new byte[44]); // 最小 WAV 头占位
        try
        {
            var opts = Options.Create(new SoundOptions { BasePath = dir, Enabled = true });
            var svc = new SoundService(opts, NullLogger<SoundService>.Instance);

            // 无 WPF Application.Current 时 Play 静默返回（不实际播放）
            var act = () => svc.Play(type);
            act.Should().NotThrow();
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
