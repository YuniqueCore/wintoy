using FuturesTrader.Presentation.Abstractions;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.Services;

/// <summary>
/// 系统级撤单注册表。选择性模式只针对可见窗口，避免在未恢复旧程序全部过滤条件前
/// 把 Space 键错误扩大为不可逆的全账户撤单。
/// </summary>
public sealed class GlobalOrderCancellationService : IGlobalOrderCancellationService
{
    private sealed record Registration(Guid Id, Func<Task> CancelAll, Func<bool> IsSelectivelyEligible);

    private readonly object _gate = new();
    private readonly Dictionary<Guid, Registration> _registrations = new();
    private readonly ILogger<GlobalOrderCancellationService> _logger;

    public GlobalOrderCancellationService(ILogger<GlobalOrderCancellationService> logger) => _logger = logger;

    public IDisposable Register(Func<Task> cancelAll, Func<bool> isSelectivelyEligible)
    {
        ArgumentNullException.ThrowIfNull(cancelAll);
        ArgumentNullException.ThrowIfNull(isSelectivelyEligible);
        var registration = new Registration(Guid.NewGuid(), cancelAll, isSelectivelyEligible);
        lock (_gate) _registrations.Add(registration.Id, registration);
        return new RegistrationLease(this, registration.Id);
    }

    public async Task<GlobalOrderCancellationResult> CancelAsync(GlobalOrderCancellationMode mode)
    {
        Registration[] targets;
        lock (_gate)
        {
            targets = _registrations.Values
                .Where(registration => mode == GlobalOrderCancellationMode.ForceAllWindows
                    || registration.IsSelectivelyEligible())
                .ToArray();
        }

        var failed = 0;
        foreach (var target in targets)
        {
            try
            {
                await target.CancelAll().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "系统级撤单派发失败，模式={Mode}", mode);
            }
        }

        _logger.LogInformation("系统级撤单请求已派发：模式={Mode} 目标窗口={Target} 失败窗口={Failed}",
            mode, targets.Length, failed);
        return new GlobalOrderCancellationResult(targets.Length, failed);
    }

    private void Unregister(Guid id)
    {
        lock (_gate) _registrations.Remove(id);
    }

    private sealed class RegistrationLease(GlobalOrderCancellationService owner, Guid id) : IDisposable
    {
        private GlobalOrderCancellationService? _owner = owner;
        private readonly Guid _id = id;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unregister(_id);
        }
    }
}
