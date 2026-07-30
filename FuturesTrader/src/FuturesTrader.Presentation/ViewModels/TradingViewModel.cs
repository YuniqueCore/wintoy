using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Domain.WindowGroups;
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
    private readonly ITradingService _trading;
    private readonly MarketDataOptions _options;
    private readonly ILogger<TradingViewModel> _logger;
    private readonly CompositeDisposable _subscriptions = new();
    private decimal _priceTick = 1m;
    private bool _disposed;
    private InstrumentWindow _config;
    private Instrument? _instrument;
    /// <summary>最近一次行情快照：用于 OrderViewModel 报单回报触发价格梯重建时复用（无需重读行情）。</summary>
    private DepthMarketData? _lastMarketData;

    public TradingViewModel(
        string instrumentCode,
        IMarketDataService marketData,
        IKeyboardOperationService keyboard,
        ISoundService sound,
        IOptions<MarketDataOptions> options,
        ILogger<TradingViewModel> logger,
        ITradingService trading,
        ILocalRiskService risk,
        IOrderValidator orderValidator,
        ILogger<OrderViewModel> orderLogger)
        : this(new InstrumentWindow { InstrumentCode = instrumentCode },
              marketData, keyboard, sound, options, logger, trading, risk, orderValidator, orderLogger)
    {
    }

    public TradingViewModel(
        InstrumentWindow config,
        IMarketDataService marketData,
        IKeyboardOperationService keyboard,
        ISoundService sound,
        IOptions<MarketDataOptions> options,
        ILogger<TradingViewModel> logger,
        ITradingService trading,
        ILocalRiskService risk,
        IOrderValidator orderValidator,
        ILogger<OrderViewModel> orderLogger)
    {
        _config = config;
        InstrumentCode = config.InstrumentCode;
        _marketData = marketData;
        _keyboard = keyboard;
        _sound = sound;
        _trading = trading;
        _options = options.Value;
        _logger = logger;
        PriceLadderLevels = _options.PriceLadderLevels;

        // 从 InstrumentWindow 33 字段初始化合约窗口配置（双向绑定，关闭时回写）
        HydrateFromConfig(config);

        // 下单区 VM：每合约独立实例，共享交易/风控/校验链单例服务
        Order = new OrderViewModel(config.InstrumentCode, trading, risk, orderValidator, orderLogger);
        // 订阅报单活跃状态变更：触发本合约价格梯重建，让 PendingOrderCount 立即更新
        Order.ActiveOrdersChanged += (_, _) => RebuildPriceLadder();

        // 推迟到 UI 线程空闲后订阅，避免构造期间行情回调竞态
        MarshalToUi(Subscribe, immediateIfNoDispatcher: true);
    }

    /// <summary>合约代码（如 ag2608）。</summary>
    public string InstrumentCode { get; }

    /// <summary>窗口标题显示名：合约码 + 期权持续时间 + 组号。
    /// 期货格式 "ag2608 · 组 3"；期权格式 "ps2609-C-36500 [10天 0807] · 组 3"。
    /// 合约元数据到达后（OnInstrumentUpdate）刷新。</summary>
    public string InstrumentDisplayName => BuildDisplayName();

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

    /// <summary>多头持仓手数（本合约聚合，CTP PosiDirection='2' Long）。</summary>
    [ObservableProperty]
    public partial int LongPosition { get; private set; }

    /// <summary>空头持仓手数（本合约聚合，CTP PosiDirection='3' Short）。</summary>
    [ObservableProperty]
    public partial int ShortPosition { get; private set; }

    /// <summary>总持仓手数（多+空，浮动栏「持」字段）。</summary>
    public int TotalPosition => LongPosition + ShortPosition;

    /// <summary>可用资金（CTP Available，浮动栏「可」字段）。</summary>
    [ObservableProperty]
    public partial decimal Available { get; private set; }

    /// <summary>投资者权益（CTP Balance - WithdrawBalance，浮动栏「权」字段）。</summary>
    [ObservableProperty]
    public partial decimal Equity { get; private set; }

    /// <summary>市值（持仓市值，浮动栏「市」字段）。</summary>
    [ObservableProperty]
    public partial decimal MarketValue { get; private set; }

    /// <summary>净盈亏（持仓盈亏 + 平仓盈亏，浮动栏「净」字段）。</summary>
    [ObservableProperty]
    public partial decimal NetProfit { get; private set; }

    /// <summary>当日手续费（CTP Commission，浮动栏辅助字段）。</summary>
    [ObservableProperty]
    public partial decimal Commission { get; private set; }

    // ── InstrumentWindow 33 字段双向绑定（关闭时回写持久化）──

    /// <summary>左键点击挂单数（ValLeft，默认 1）。</summary>
    [ObservableProperty] public partial int ValLeft { get; set; } = 1;

    /// <summary>右键点击挂单数（ValRight，默认 2，新手禁用）。</summary>
    [ObservableProperty] public partial int ValRight { get; set; } = 2;

    /// <summary>单行高度（RowHeight，像素）。</summary>
    [ObservableProperty] public partial int RowHeight { get; set; } = 12;

    /// <summary>卖一价靠左（RboA）。</summary>
    [ObservableProperty] public partial bool RboA { get; set; }

    /// <summary>买一价靠左（RboB，默认 true）。</summary>
    [ObservableProperty] public partial bool RboB { get; set; } = true;

    /// <summary>Chg Nearby：每成交一手暂停约 1 秒挂单（推荐勾选）。</summary>
    [ObservableProperty] public partial bool CbNearby { get; set; }

    /// <summary>OnlyOpen：开仓模式（与浮动栏「仓/平」联动，true=开仓）。</summary>
    [ObservableProperty] public partial bool CbOnlyOpen { get; set; }

    /// <summary>窄模式（NarrowMode）。</summary>
    [ObservableProperty] public partial bool NarrowMode { get; set; }

    /// <summary>CntrbySprd 主开关。</summary>
    [ObservableProperty] public partial bool CbCntrbySprd { get; set; }

    /// <summary>CntrbySprd 扩展开关。</summary>
    [ObservableProperty] public partial bool CbCntrbySprdEx { get; set; }

    /// <summary>Cd 锁定。</summary>
    [ObservableProperty] public partial bool CbCdLock { get; set; }

    /// <summary>白格（CbBgds，默认 true）。</summary>
    [ObservableProperty] public partial bool CbBgds { get; set; } = true;

    /// <summary>涨跌停锁（CbZdtLock，默认 true）。</summary>
    [ObservableProperty] public partial bool CbZdtLock { get; set; } = true;

    /// <summary>价差锁定合约码（CntrbySprdId）。</summary>
    [ObservableProperty] public partial string CntrbySprdId { get; set; } = string.Empty;

    /// <summary>价差锁定 Pt 值（CntrbySprdPt）。</summary>
    [ObservableProperty] public partial int CntrbySprdPt { get; set; }

    /// <summary>价差因子（CntrbySprdFctn，默认 1）。</summary>
    [ObservableProperty] public partial int CntrbySprdFctn { get; set; } = 1;

    /// <summary>挂单模式：true=A（单方向单点），false=B（单方向多点）。</summary>
    [ObservableProperty] public partial bool IsChgOrderA { get; set; } = true;

    /// <summary>M-OrderX：提前挂单（禁止使用）。</summary>
    [ObservableProperty] public partial bool MOrderX { get; set; }

    /// <summary>成交标识开关。</summary>
    [ObservableProperty] public partial bool ShowTradeMark { get; set; } = true;

    // ── 底部状态区 ──

    /// <summary>已使用撤单数（底部「N K」展示）。</summary>
    [ObservableProperty] public partial int CancelCount { get; private set; }

    /// <summary>已成交开仓数（底部「C:N」）。</summary>
    [ObservableProperty] public partial int TradeOpenCount { get; private set; }

    /// <summary>已成交平仓数（底部「T:N」）。</summary>
    [ObservableProperty] public partial int TradeCloseCount { get; private set; }

    /// <summary>开平仓模式标识（true=开仓显示「O」，false=平仓显示「P」）。</summary>
    public string OpenCloseMark => CbOnlyOpen ? "O" : "P";

    /// <summary>从 InstrumentWindow 33 字段初始化 VM 状态（构造时调用）。</summary>
    private void HydrateFromConfig(InstrumentWindow c)
    {
        ValLeft = c.ValLeft;
        ValRight = c.ValRight;
        RowHeight = c.RowHeight;
        RboA = c.RboA;
        RboB = c.RboB;
        CbNearby = c.CbNearby;
        CbOnlyOpen = c.CbOnlyOpen;
        NarrowMode = c.NarrowMode;
        CbCntrbySprd = c.CbCntrbySprd;
        CbCntrbySprdEx = c.CbCntrbySprdEx;
        CbCdLock = c.CbCdLock;
        CbBgds = c.CbBgds;
        CbZdtLock = c.CbZdtLock;
        CntrbySprdId = c.CntrbySprdId;
        CntrbySprdPt = c.CntrbySprdPt;
        CntrbySprdFctn = c.CntrbySprdFctn;
    }

    /// <summary>将 VM 当前状态回写为 InstrumentWindow（窗口关闭时持久化）。</summary>
    public InstrumentWindow ToInstrumentWindow()
    {
        return _config with
        {
            ValLeft = ValLeft,
            ValRight = ValRight,
            RowHeight = RowHeight,
            RboA = RboA,
            RboB = RboB,
            CbNearby = CbNearby,
            CbOnlyOpen = CbOnlyOpen,
            NarrowMode = NarrowMode,
            CbCntrbySprd = CbCntrbySprd,
            CbCntrbySprdEx = CbCntrbySprdEx,
            CbCdLock = CbCdLock,
            CbBgds = CbBgds,
            CbZdtLock = CbZdtLock,
            CntrbySprdId = CntrbySprdId,
            CntrbySprdPt = CntrbySprdPt,
            CntrbySprdFctn = CntrbySprdFctn
        };
    }

    /// <summary>价格梯左键点击：按 ValLeft 量挂单。红区(Ask)挂空单 Sell，蓝区(Bid)挂多单 Buy。</summary>
    public async Task OnPriceLeftClickedAsync(decimal price, PriceZone zone)
    {
        if (ValLeft <= 0) return;
        await PlaceOrderFromClickAsync(price, zone, ValLeft);
    }

    /// <summary>价格梯右键点击：按 ValRight 量挂单（新手禁用）。</summary>
    public async Task OnPriceRightClickedAsync(decimal price, PriceZone zone)
    {
        if (ValRight <= 0) return;
        await PlaceOrderFromClickAsync(price, zone, ValRight);
    }

    /// <summary>
    /// 撤销指定价位的所有挂单：用户在 PriceListControl 第 0 列点击（挂单数 &gt;0）时调用。
    /// </summary>
    public Task CancelOrdersAtPriceAsync(decimal price) => Order.CancelOrdersAtPriceAsync(price);

    /// <summary>
    /// 全局撤销当前合约的所有活动报单：键盘空格触发，对齐 0527.exe 全局撤单习惯。
    /// </summary>
    public Task CancelAllOrdersAsync() => Order.CancelAllOrdersAsync();

    /// <summary>按点击区域 + 数量下单：红区=Sell 空单，蓝区=Buy 多单，中心=按 OnlyOpen 决定。</summary>
    private async Task PlaceOrderFromClickAsync(decimal price, PriceZone zone, int volume)
    {
        try
        {
            // 红区(Ask/上方) → 卖出（空单）；蓝区(Bid/下方) → 买入（多单）
            var direction = zone == PriceZone.Ask ? Direction.Sell : Direction.Buy;
            var offset = CbOnlyOpen ? OffsetFlag.Open : OffsetFlag.Close;

            Order.Direction = direction;
            Order.OffsetFlag = offset;
            Order.Price = price;
            Order.Quantity = volume;
            await Order.OrderCommand.ExecuteAsync(null);
            _logger.LogInformation("价格点击下单：{Instrument} {Dir} {Off} {Price} × {Vol}",
                InstrumentCode, direction, offset, price, volume);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "价格点击下单失败 {Instrument}", InstrumentCode);
        }
    }

    /// <summary>CbOnlyOpen 变更时通知 OpenCloseMark 刷新。</summary>
    partial void OnCbOnlyOpenChanged(bool value) => OnPropertyChanged(nameof(OpenCloseMark));

    /// <summary>订阅行情流 + 交易流（持仓/资金/合约元数据）并初始化连接状态监听。</summary>
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

            // 持仓流 → 过滤本合约 → 按方向覆盖式更新（CTP 每查询一次推送一批快照）
            var posSub = _trading.PositionStream
                .Where(p => string.Equals(p.InstrumentId, InstrumentCode, StringComparison.Ordinal))
                .Subscribe(OnPositionUpdate, ex => _logger.LogError(ex, "持仓流出错 {Instrument}", InstrumentCode));
            _subscriptions.Add(posSub);

            // 资金流 → 单条快照覆盖（CTP 资金账户通常单条）
            var accSub = _trading.AccountStream.Subscribe(OnAccountUpdate, ex => _logger.LogError(ex, "资金流出错"));
            _subscriptions.Add(accSub);

            // 合约元数据流 → 过滤本合约 → 更新 PriceTick（替换硬编码 1m），同步给 Order 做价格校验
            var instSub = _trading.InstrumentStream
                .Where(i => string.Equals(i.InstrumentId, InstrumentCode, StringComparison.Ordinal))
                .Subscribe(OnInstrumentUpdate, ex => _logger.LogError(ex, "合约元数据流出错 {Instrument}", InstrumentCode));
            _subscriptions.Add(instSub);

            // 确保已连接，然后订阅本合约行情
            _ = EnsureSubscribedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "订阅行情失败 {Instrument}", InstrumentCode);
        }
    }

    /// <summary>持仓回报到达：按方向覆盖式更新（同方向多条按最新快照）。</summary>
    private void OnPositionUpdate(Position position)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            switch (position.Direction)
            {
                case Direction.Buy: LongPosition = position.TotalPosition; break;
                case Direction.Sell: ShortPosition = position.TotalPosition; break;
            }
            OnPropertyChanged(nameof(TotalPosition));
        });
    }

    /// <summary>资金账户回报到达：覆盖式更新浮动栏资金字段。</summary>
    private void OnAccountUpdate(TradingAccount account)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            Available = account.Available;
            Equity = account.Equity;
            MarketValue = account.MarketValue;
            NetProfit = account.PositionProfit + account.CloseProfit;
            Commission = account.Commission;
        });
    }

    /// <summary>合约元数据回报到达：保存 Instrument、更新 PriceTick 并刷新标题显示。</summary>
    private void OnInstrumentUpdate(Instrument instrument)
    {
        if (_disposed) return;
        MarshalToUi(() =>
        {
            if (_disposed) return;
            _instrument = instrument;
            _priceTick = instrument.PriceTick > 0 ? instrument.PriceTick : 1m;
            Order.PriceTick = _priceTick;
            OnPropertyChanged(nameof(InstrumentDisplayName));
        });
    }

    /// <summary>构造窗口标题显示名：合约码 + 期权后缀 + 组号。</summary>
    private string BuildDisplayName()
    {
        var suffix = _instrument is { IsOptions: true } opt
            ? FormatOptionsSuffix(opt.ExpireDate, DateTime.Today)
            : string.Empty;
        var group = _config.GroupId > 0 ? $" · 组 {_config.GroupId}" : string.Empty;
        return string.IsNullOrEmpty(suffix)
            ? $"{InstrumentCode}{group}"
            : $"{InstrumentCode} {suffix}{group}";
    }

    /// <summary>期权持续时间后缀：[剩余天数天 到期MMDD]。today 参数化便于单元测试。</summary>
    internal static string FormatOptionsSuffix(string expireDate, DateTime today)
    {
        if (!DateTime.TryParseExact(expireDate, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var expire))
            return string.Empty;
        var days = Math.Max(0, (expire - today).Days);
        return $"[{days}天 {expire:MMdd}]";
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
            _lastMarketData = data;
            PriceLadder = data.ToPriceLadder(_priceTick, PriceLadderLevels, BuildPendingByPrice());
            OpenPrice = data.OpenPrice;
            HighPrice = data.HighestPrice;
            LowPrice = data.LowestPrice;
            Volume = (long)data.Volume;
            OpenInterest = (long)data.OpenInterest;
            UpdateTime = data.UpdateTime.ToString("HH:mm:ss") + "." + data.UpdateMillisec.ToString("D3");
        });
    }

    /// <summary>
    /// 由 OrderViewModel.ActiveOrdersChanged 触发的价格梯重建：复用最近一次行情快照，
    /// 按当前活跃报单聚合（价位 → 数量）传入 ToPriceLadder，让 PriceLevel.PendingOrderCount 即时刷新。
    /// </summary>
    private void RebuildPriceLadder()
    {
        if (_disposed || _lastMarketData is null) return;
        MarshalToUi(() =>
        {
            if (_disposed || _lastMarketData is null) return;
            PriceLadder = _lastMarketData.ToPriceLadder(_priceTick, PriceLadderLevels, BuildPendingByPrice());
        });
    }

    /// <summary>
    /// 聚合当前活跃报单为「价格 → 数量」字典（按 PriceTick 对齐 key）。
    /// 同一价位上多笔挂单合并为一格显示数。
    /// </summary>
    private IReadOnlyDictionary<decimal, int> BuildPendingByPrice()
    {
        if (Order is null) return new Dictionary<decimal, int>();
        var snapshot = Order.ActiveOrders;
        if (snapshot.Count == 0) return new Dictionary<decimal, int>();
        var dict = new Dictionary<decimal, int>(snapshot.Count);
        foreach (var (_, info) in snapshot)
        {
            // 价位按 PriceTick 对齐（避免浮点漂移导致 ToPriceLadder LookupPending 容差扫描不全）
            var alignedPrice = _priceTick > 0
                ? Math.Round(info.Price / _priceTick) * _priceTick
                : info.Price;
            dict[alignedPrice] = dict.GetValueOrDefault(alignedPrice) + 1;
        }
        return dict;
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
