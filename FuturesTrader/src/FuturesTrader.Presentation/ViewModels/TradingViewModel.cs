using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Presentation.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// 合约交易窗口 ViewModel（TYYWin 复刻）：每合约一个实例，由 WindowManager 用 ActivatorUtilities 创建。
/// 构造时订阅本合约行情流 → Dispatcher 刷新 <see cref="PriceLadder"/>（价差居中）+ 摘要字段。
/// <see cref="InstrumentCode"/> 为合约代码；<see cref="PriceLadderLevels"/> 控制上下档位数（默认 5）。
/// 行情推送在 CTP/Mock 工作线程触发，回调内通过 <see cref="MarshalToUi"/> 切回 UI 线程刷新。
/// <para>
/// <see cref="Order"/> 为下单区 VM（买卖/开平/价格/数量 + 报单/撤单），行情到达时同步 PriceTick 给它做价格校验。
/// </para>
/// </summary>
public sealed partial class TradingViewModel : ObservableObject, IDisposable
{
    private readonly IMarketDataService _marketData;
    private readonly IKeyboardOperationService _keyboard;
    private readonly ISoundService _sound;
    private readonly MarketDataOptions _options;
    private readonly ILogger<TradingViewModel> _logger;
    private readonly CompositeDisposable _subscriptions = new();
    private decimal _priceTick = 1m;
    private bool _disposed;

    public TradingViewModel(
        string instrumentCode,
        IMarketDataService marketData,
        IKeyboardOperationService keyboard,
        ISoundService sound,
        IOptions<MarketDataOptions> options,
        ILogger<TradingViewModel> logger,
        ITradingService trading,
        ILocalRiskService risk,
        ILogger<OrderViewModel> orderLogger)
    {
        InstrumentCode = instrumentCode;
        _marketData = marketData;
        _keyboard = keyboard;
        _sound = sound;
        _options = options.Value;
        _logger = logger;
        PriceLadderLevels = _options.PriceLadderLevels;

        // 下单区 VM：每合约独立实例，共享交易/风控单例服务
        Order = new OrderViewModel(instrumentCode, trading, risk, orderLogger);

        // 推迟到 UI 线程空闲后订阅，避免构造期间行情回调竞态
        MarshalToUi(Subscribe, immediateIfNoDispatcher: true);
    }

    /// <summary>合约代码（如 ag2608）。</summary>
    public string InstrumentCode { get; }

    /// <summary>价格梯上下档位数（从 MarketDataOptions 绑定）。</summary>
    public int PriceLadderLevels { get; }

    /// <summary>下单区 VM（买卖/开平/价格/数量 + 报单/撤单）。XAML 下单面板 DataContext={Binding Order}。</summary>
    public OrderViewModel Order { get; }

    [ObservableProperty]
    public partial PriceLadder? PriceLadder { get; private set; }

    [ObservableProperty]
    public partial decimal OpenPrice { get; private set; }

    [ObservableProperty]
    public partial decimal HighPrice { get; private set; }

    [ObservableProperty]
    public partial decimal LowPrice { get; private set; }

    [ObservableProperty]
    public partial long Volume { get; private set; }

    [ObservableProperty]
    public partial long OpenInterest { get; private set; }

    [ObservableProperty]
    public partial string UpdateTime { get; private set; } = "--:--:--";

    [ObservableProperty]
    public partial string ConnectionState { get; private set; } = "未连接";

    /// <summary>订阅行情流并初始化连接状态监听。</summary>
    private void Subscribe()
    {
        if (_disposed) return;
        try
        {
            // 连接状态流 → ConnectionState 字符串（UI 反馈）
            var connSub = _marketData.ConnectionStream.Subscribe(
                state => MarshalToUi(() => ConnectionState = StateToText(state)),
                ex => _logger.LogError(ex, "连接状态流出错 {Instrument}", InstrumentCode));
            _subscriptions.Add(connSub);

            // 行情流 → 过滤本合约 → Dispatcher 刷新 UI
            var mdSub = _marketData.MarketDataStream
                .Where(d => d.InstrumentId == InstrumentCode)
                .Subscribe(
                    OnMarketData,
                    ex => _logger.LogError(ex, "行情流出错 {Instrument}", InstrumentCode));
            _subscriptions.Add(mdSub);

            // 确保已连接，然后订阅本合约行情
            _ = EnsureSubscribedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订阅行情失败 {Instrument}", InstrumentCode);
        }
    }

    /// <summary>确保行情服务已连接并订阅本合约（幂等）。</summary>
    private async Task EnsureSubscribedAsync()
    {
        try
        {
            if (_marketData.CurrentState is not Domain.MarketData.ConnectionState.Connected)
                await _marketData.ConnectAsync();
            await _marketData.SubscribeAsync(new[] { InstrumentCode });
            _logger.LogInformation("已订阅 {Instrument} 行情", InstrumentCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订阅合约行情失败 {Instrument}", InstrumentCode);
        }
    }

    /// <summary>行情快照到达：切回 UI 线程更新 PriceLadder + 摘要字段。</summary>
    private void OnMarketData(DepthMarketData data)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            PriceLadder = data.ToPriceLadder(_priceTick, PriceLadderLevels);
            OpenPrice = data.OpenPrice;
            HighPrice = data.HighestPrice;
            LowPrice = data.LowestPrice;
            Volume = (long)data.Volume;
            OpenInterest = (long)data.OpenInterest;
            UpdateTime = data.UpdateTime.ToString("HH:mm:ss") + "." + data.UpdateMillisec.ToString("D3");
        });
    }

    /// <summary>注册 Up/Down 导航快捷键到 PriceList（M3 扩展时加买卖热键）。</summary>
    public void RegisterKeyboardShortcuts(int maxRowIndex)
    {
        _keyboard.Register(
            new KeyGesture(Key.Up),
            () => _keyboard.MoveSelection(-1, maxRowIndex),
            "上移选中价位");
        _keyboard.Register(
            new KeyGesture(Key.Down),
            () => _keyboard.MoveSelection(1, maxRowIndex),
            "下移选中价位");
    }

    /// <summary>窗口关闭时退订（释放订阅 + 下单 VM，避免泄漏）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _ = _marketData.UnsubscribeAsync(new[] { InstrumentCode });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "退订行情失败 {Instrument}", InstrumentCode);
        }
        _subscriptions.Dispose();
        Order.Dispose();
    }

    private static string StateToText(Domain.MarketData.ConnectionState state) => state switch
    {
        Domain.MarketData.ConnectionState.Connected => "已连接",
        Domain.MarketData.ConnectionState.Connecting => "连接中…",
        Domain.MarketData.ConnectionState.Reconnecting r => $"重连中({r.Attempt})",
        Domain.MarketData.ConnectionState.Failed f => $"失败:{f.Error}",
        _ => "未连接"
    };

    /// <summary>把 action 调度到 UI 线程执行；无 WPF 应用上下文（单元测试）则直接内联执行。
    /// <paramref name="immediateIfNoDispatcher"/> 为 true 时，无 dispatcher 也立即执行（构造期 Subscribe 用）。</summary>
    private static void MarshalToUi(Action action, bool immediateIfNoDispatcher = false)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            if (immediateIfNoDispatcher) action();
            return;
        }
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
