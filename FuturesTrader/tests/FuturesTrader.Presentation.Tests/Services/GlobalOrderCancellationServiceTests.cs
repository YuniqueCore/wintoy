using FluentAssertions;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.Presentation.Tests.Services;

public class GlobalOrderCancellationServiceTests
{
    [Fact]
    public async Task Selective_mode_targets_only_visible_window_registrations()
    {
        var service = new GlobalOrderCancellationService(NullLogger<GlobalOrderCancellationService>.Instance);
        var visibleCalls = 0;
        var hiddenCalls = 0;
        using var visible = service.Register(() =>
        {
            visibleCalls++;
            return Task.CompletedTask;
        }, () => true);
        using var hidden = service.Register(() =>
        {
            hiddenCalls++;
            return Task.CompletedTask;
        }, () => false);

        var result = await service.CancelAsync(GlobalOrderCancellationMode.SelectiveVisibleWindows);

        result.Should().Be(new GlobalOrderCancellationResult(1, 0));
        visibleCalls.Should().Be(1);
        hiddenCalls.Should().Be(0);
    }

    [Fact]
    public async Task Force_mode_targets_hidden_and_visible_window_registrations()
    {
        var service = new GlobalOrderCancellationService(NullLogger<GlobalOrderCancellationService>.Instance);
        var calls = 0;
        using var visible = service.Register(() =>
        {
            calls++;
            return Task.CompletedTask;
        }, () => true);
        using var hidden = service.Register(() =>
        {
            calls++;
            return Task.CompletedTask;
        }, () => false);

        var result = await service.CancelAsync(GlobalOrderCancellationMode.ForceAllWindows);

        result.Should().Be(new GlobalOrderCancellationResult(2, 0));
        calls.Should().Be(2);
    }

    [Fact]
    public async Task Disposed_registration_is_not_targeted()
    {
        var service = new GlobalOrderCancellationService(NullLogger<GlobalOrderCancellationService>.Instance);
        var calls = 0;
        var registration = service.Register(() =>
        {
            calls++;
            return Task.CompletedTask;
        }, () => true);
        registration.Dispose();

        var result = await service.CancelAsync(GlobalOrderCancellationMode.ForceAllWindows);

        result.Should().Be(new GlobalOrderCancellationResult(0, 0));
        calls.Should().Be(0);
    }
}
