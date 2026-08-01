using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Domain.Trading;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 资金持仓摘要 ViewModel：订阅 <see cref="ITradingService.AccountStream"/> + <see cref="PositionStream"/> + <see cref="TradeStream"/>，
/// 聚合为浮动栏底部「市/净/可/持/权/手」六项实时展示。
/// <para>
/// 字段映射（见 <c>floating.main.window.md</c>）：
/// 市=<see cref="MarketValue"/>，净=<see cref="NetProfit"/>，可=<see cref="Available"/>，
/// 持=<see cref="PositionLots"/>，权=<see cref="Equity"/>，手=<see cref="TradeLots"/>。
/// </para>
/// <para>
/// CTP 回调在工作线程触发，通过 <see cref="MarshalToUi"/> 切回 UI 线程刷新属性。
/// </para>
/// </summary>
public sealed partial class AccountSummaryViewModel : ObservableObject, IDisposable
{
    private readonly ITradingService _trading;
    private readonly ILogger<AccountSummaryViewModel> _logger;
    private readonly CompositeDisposable _subscriptions = new();
    private readonly Dictionary<(string InstrumentId, Direction Direction), int> _positions = new();
    private bool _disposed;

    public AccountSummaryViewModel(ITradingService trading, ILogger<AccountSummaryViewModel> logger)
    {
        _trading = trading;
        _logger = logger;
        Subscribe();
    }

    /// <summary>市：市值（持仓按最新价计算）。</summary>
    [ObservableProperty] private decimal _marketValue;

    /// <summary>净：净盈亏（持仓盈亏 + 平仓盈亏）。</summary>
    [ObservableProperty] private decimal _netProfit;

    /// <summary>可：可用资金。</summary>
    [ObservableProperty] private decimal _available;

    /// <summary>持：持仓手数（多空绝对值合计）。</summary>
    [ObservableProperty] private int _positionLots;

    /// <summary>权：投资者权益。</summary>
    [ObservableProperty] private decimal _equity;

    /// <summary>手：当日成交手数（累计）。</summary>
    [ObservableProperty] private int _tradeLots;

    private void Subscribe()
    {
        // 资金账户流：更新市/净/可/权
        var accountSub = _trading.AccountStream.Subscribe(
            acct => MarshalToUi(() =>
            {
                MarketValue = acct.MarketValue;
                NetProfit = acct.PositionProfit + acct.CloseProfit;
                Available = acct.Available;
                Equity = acct.Equity;
            }),
            ex => _logger.LogError(ex, "AccountStream 订阅异常"));
        _subscriptions.Add(accountSub);

        // 持仓流：按合约聚合持仓手数（每条 Position 更新对应合约的总持仓，再求和）
        var positionSub = _trading.PositionStream.Subscribe(
            pos => MarshalToUi(() =>
            {
                _positions[(pos.InstrumentId, pos.Direction)] = Math.Abs(pos.TotalPosition);
                PositionLots = _positions.Values.Sum();
            }),
            ex => _logger.LogError(ex, "PositionStream 订阅异常"));
        _subscriptions.Add(positionSub);

        // 成交流：累计成交手数
        var tradeSub = _trading.TradeStream.Subscribe(
            trade => MarshalToUi(() => TradeLots += trade.Volume),
            ex => _logger.LogError(ex, "TradeStream 订阅异常"));
        _subscriptions.Add(tradeSub);
    }

    /// <summary>把 action 调度到 UI 线程执行；无 WPF 应用上下文（单元测试）则直接内联执行。</summary>
    private static void MarshalToUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { action(); return; }
        if (dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _subscriptions.Dispose();
    }
}
