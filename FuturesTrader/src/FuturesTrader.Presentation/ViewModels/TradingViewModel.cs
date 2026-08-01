using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
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
/// <see cref="InstrumentCode"/> 为合约代码；空方/多方显示行数由每窗口配置控制（默认各 30）。
/// 行情推送在 CTP/Mock 工作线程触发，回调内通过 <see cref="MarshalToUi"/> 切回 UI 线程刷新。
/// <para>
/// <see cref="Order"/> 为下单区 VM（买卖/开平/价格/数量 + 报单/撤单），行情到达时同步 PriceTick 给它做价格校验。
/// </para>
/// </summary>
public sealed partial class TradingViewModel : ObservableObject, IDisposable
{
    private readonly IMarketDataService _marketData;
    private readonly ISoundService _sound;
    private readonly ITradingService _trading;
    private readonly MarketDataOptions _options;
    private readonly LegacyTradingRuntime _legacyTradingRuntime;
    private readonly ILogger<TradingViewModel> _logger;
    private readonly CompositeDisposable _subscriptions = new();
    private decimal _priceTick = 1m;
    private bool _disposed;
    private bool _synchronizingOrderMode;
    private InstrumentWindow _config;
    private Instrument? _instrument;
    private PriceLadderDirectionMap _priceLadderDirectionMap = new();
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
        ILogger<OrderViewModel> orderLogger,
        LegacyTradingRuntime? legacyTradingRuntime = null)
        : this(new InstrumentWindow { InstrumentCode = instrumentCode },
              marketData, keyboard, sound, options, logger, trading, risk, orderValidator, orderLogger, legacyTradingRuntime)
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
        ILogger<OrderViewModel> orderLogger,
        LegacyTradingRuntime? legacyTradingRuntime = null)
    {
        _config = config;
        InstrumentCode = config.InstrumentCode;
        _marketData = marketData;
        _sound = sound;
        _trading = trading;
        _options = options.Value;
        _legacyTradingRuntime = legacyTradingRuntime ?? new LegacyTradingRuntime();
        _logger = logger;
        // 从 InstrumentWindow 字段初始化合约窗口配置（双向绑定，关闭时回写）
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

    /// <summary>窗口标题显示名：合约名称 - 合约代码 + 期权持续时间。
    /// 期货格式 "白银2608 - ag2608"；期权格式 "豆粕期权 - m2609-C-3200 [10天 0807]"。
    /// 合约元数据到达后（OnInstrumentUpdate）刷新。</summary>
    public string InstrumentDisplayName => BuildDisplayName();

    /// <summary>下单区 VM（买卖/开平/价格/数量 + 报单/撤单）。XAML 下单面板 DataContext={Binding Order}。</summary>
    public OrderViewModel Order { get; }

    /// <summary>
    /// 当前合约的物理交易侧到 CTP 方向映射。默认列 1=Buy、列 3=Sell；
    /// 由合约适配层识别到旧程序的反转标志后可替换，不能从行情显示颜色推断。
    /// </summary>
    public PriceLadderDirectionMap PriceLadderDirectionMap => _priceLadderDirectionMap;

    [ObservableProperty]
    public partial PriceLadder? PriceLadder { get; private set; }

    /// <summary>配置栏价格梯结构摘要，避免用虚拟化 UI 容器数量推断真实行数。</summary>
    public string PriceLadderStructureSummary => PriceLadder is null
        ? "自动 — 格"
        : $"自动 {PriceLadder.UnquotedRowCount} 格 · 共 {PriceLadder.Rows.Count} 格";

    /// <summary>是否显示无人报价价位行。只影响呈现，不改变价格梯交易侧和下单规则。</summary>
    [ObservableProperty]
    public partial bool ShowWhiteGrid { get; set; } = true;

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

    /// <summary>卖一向上的空方价格行数；每个合约窗口独立持久化。</summary>
    [ObservableProperty] public partial int AskQuoteRowCount { get; set; } = 30;

    /// <summary>买一向下的多方价格行数；每个合约窗口独立持久化。</summary>
    [ObservableProperty] public partial int BidQuoteRowCount { get; set; } = 30;

    /// <summary>旧 RBOA 单选状态：A 模式（同合约同方向替换）。</summary>
    [ObservableProperty] public partial bool RboA { get; set; }

    /// <summary>旧 RBOB 单选状态：B 模式（普通追加，默认 true）。</summary>
    [ObservableProperty] public partial bool RboB { get; set; } = true;

    /// <summary>Chg Nearby：行情变化后短时间内阻止该交易侧误点。</summary>
    [ObservableProperty] public partial bool CbNearby { get; set; }

    /// <summary>OnlyOpen：开仓模式（与浮动栏「仓/平」联动，true=开仓）。</summary>
    [ObservableProperty] public partial bool CbOnlyOpen { get; set; }

    /// <summary>旧 CBOC：B 平仓时是否保留同方向开仓挂单。</summary>
    [ObservableProperty] public partial bool CbOc { get; set; }

    /// <summary>
    /// CBOC 的旧 XML 仅在 RunMode=1/2 的写出分支出现。RunMode=3 的 B 平仓路径仍会读取运行时控件，
    /// 但没有已证实的持久化入口，故当前端口不把它展示为可保存配置。
    /// </summary>
    public bool IsCbOcConfigurationPersisted => _legacyTradingRuntime.PersistsCbOc;

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

    /// <summary>挂单模式：由 RBOA/RBOB 单选状态派生；B 是 Users.xml 的默认值。</summary>
    [ObservableProperty]
    public partial OrderPlacementMode OrderPlacementMode { get; set; } = OrderPlacementMode.Append;

    /// <summary>供现有 A 单选 UI 使用的派生属性。</summary>
    public bool IsChgOrderA
    {
        get => OrderPlacementMode == OrderPlacementMode.ReplaceSameDirection;
        set
        {
            if (value) OrderPlacementMode = OrderPlacementMode.ReplaceSameDirection;
        }
    }

    /// <summary>供现有 B 单选 UI 使用的派生属性。</summary>
    public bool IsChgOrderB
    {
        get => OrderPlacementMode == OrderPlacementMode.Append;
        set
        {
            if (value) OrderPlacementMode = OrderPlacementMode.Append;
        }
    }

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
        RowHeight = Math.Clamp(c.RowHeight, 10, 32);
        AskQuoteRowCount = Math.Clamp(c.AskQuoteRowCount, 5, 100);
        BidQuoteRowCount = Math.Clamp(c.BidQuoteRowCount, 5, 100);
        SetOrderPlacementModeFromLegacyRadio(c.RboA, c.RboB);
        CbNearby = c.CbNearby;
        CbOnlyOpen = c.CbOnlyOpen;
        CbOc = _legacyTradingRuntime.PersistsCbOc && c.CbOc;
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
            RowHeight = Math.Clamp(RowHeight, 10, 32),
            AskQuoteRowCount = Math.Clamp(AskQuoteRowCount, 5, 100),
            BidQuoteRowCount = Math.Clamp(BidQuoteRowCount, 5, 100),
            RboA = RboA,
            RboB = RboB,
            CbNearby = CbNearby,
            CbOnlyOpen = CbOnlyOpen,
            CbOc = _legacyTradingRuntime.PersistsCbOc && CbOc,
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

    /// <summary>价格梯左键点击：只选择 ValLeft 手数，方向由物理交易侧映射。</summary>
    public Task OnPriceLeftClickedAsync(decimal price, PriceLadderTradeSide side)
    {
        return PlaceOrderFromClickAsync(price, side, MouseQuantityButton.Left);
    }

    /// <summary>价格梯右键点击：只选择 ValRight 手数，方向由物理交易侧映射。</summary>
    public Task OnPriceRightClickedAsync(decimal price, PriceLadderTradeSide side)
    {
        return PlaceOrderFromClickAsync(price, side, MouseQuantityButton.Right);
    }

    /// <summary>
    /// 撤销指定价位的所有挂单：用户在 PriceListControl 第 0 列点击（挂单数 &gt;0）时调用。
    /// </summary>
    public Task CancelOrdersAtPriceAsync(decimal price) => Order.CancelOrdersAtPriceAsync(price);

    /// <summary>
    /// 撤销当前合约的所有活动报单。系统级 Space 由窗口宿主的全局撤单服务处理。
    /// </summary>
    public Task CancelAllOrdersAsync() => Order.CancelAllOrdersAsync();

    /// <summary>按鼠标数量键和物理交易侧下单，行情显示区不参与方向决策。</summary>
    private async Task PlaceOrderFromClickAsync(
        decimal price,
        PriceLadderTradeSide side,
        MouseQuantityButton mouseButton)
    {
        try
        {
            var unsupportedReason = _legacyTradingRuntime.GetUnsupportedPriceLadderOrderReason();
            if (unsupportedReason is not null)
            {
                Order.ReportPriceLadderOrderBlocked(unsupportedReason);
                return;
            }

            var volume = mouseButton == MouseQuantityButton.Left ? ValLeft : ValRight;
            if (volume <= 0)
            {
                Order.ReportPriceLadderOrderBlocked(
                    mouseButton == MouseQuantityButton.Left
                        ? "左键挂单数量必须大于 0"
                        : "右键挂单数量必须大于 0");
                return;
            }
            var direction = _priceLadderDirectionMap.Resolve(side);
            await Order.PlacePriceLadderOrderAsync(
                direction,
                price,
                volume,
                side,
                OrderPlacementMode,
                CbOnlyOpen,
                CbNearby,
                _options.NearbyProtectionMs,
                _legacyTradingRuntime.ResolveBModeClosePolicy(CbOc));
            _logger.LogInformation("价格点击下单：{Instrument} {Side} {Mouse} {Mode} {Price} × {Vol}",
                InstrumentCode, side, mouseButton, OrderPlacementMode, price, volume);
        }
        catch (Exception ex)
        {
            Order.ReportPriceLadderOrderFailure(ex.Message);
            _logger.LogError(ex, "价格点击下单失败 {Instrument}", InstrumentCode);
        }
    }

    /// <summary>CbOnlyOpen 变更时通知 OpenCloseMark 刷新。</summary>
    partial void OnCbOnlyOpenChanged(bool value) => OnPropertyChanged(nameof(OpenCloseMark));

    partial void OnPriceLadderChanged(PriceLadder? value) =>
        OnPropertyChanged(nameof(PriceLadderStructureSummary));

    partial void OnAskQuoteRowCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 5, 100);
        if (value != clamped)
        {
            AskQuoteRowCount = clamped;
            return;
        }
        RebuildPriceLadder();
    }

    partial void OnBidQuoteRowCountChanged(int value)
    {
        var clamped = Math.Clamp(value, 5, 100);
        if (value != clamped)
        {
            BidQuoteRowCount = clamped;
            return;
        }
        RebuildPriceLadder();
    }

    partial void OnOrderPlacementModeChanged(OrderPlacementMode value)
    {
        if (!_synchronizingOrderMode)
            SynchronizeOrderMode(value);
        OnPropertyChanged(nameof(IsChgOrderA));
        OnPropertyChanged(nameof(IsChgOrderB));
    }

    partial void OnRboAChanged(bool value)
    {
        if (!_synchronizingOrderMode)
            SynchronizeOrderMode(value ? OrderPlacementMode.ReplaceSameDirection : OrderPlacementMode.Append);
    }

    partial void OnRboBChanged(bool value)
    {
        if (!_synchronizingOrderMode)
            SynchronizeOrderMode(value ? OrderPlacementMode.Append : OrderPlacementMode.ReplaceSameDirection);
    }

    /// <summary>
    /// RBOA/RBOB 是旧版互斥 RadioButton 的持久化投影。损坏 XML 出现“双真”或“双假”时，
    /// 规范化为 B，避免在未明确选择 A 时自动撤掉同方向订单。
    /// </summary>
    private void SetOrderPlacementModeFromLegacyRadio(bool rboA, bool rboB) =>
        SynchronizeOrderMode(rboA && !rboB
            ? OrderPlacementMode.ReplaceSameDirection
            : OrderPlacementMode.Append);

    /// <summary>从唯一的领域模式同步旧 XML 单选字段，避免两个可变布尔状态分叉。</summary>
    private void SynchronizeOrderMode(OrderPlacementMode mode)
    {
        if (_synchronizingOrderMode) return;

        _synchronizingOrderMode = true;
        try
        {
            RboA = mode == OrderPlacementMode.ReplaceSameDirection;
            RboB = mode == OrderPlacementMode.Append;
            OrderPlacementMode = mode;
        }
        finally
        {
            _synchronizingOrderMode = false;
        }
    }

    /// <summary>供合约适配层设置旧程序所支持的交易侧反转映射。</summary>
    public void SetPriceLadderDirectionMap(PriceLadderDirectionMap map) => _priceLadderDirectionMap = map;

    /// <summary>订阅行情流 + 交易流（持仓/资金/合约元数据）并初始化连接状态监听。</summary>
    private void Subscribe()
    {
        if (_disposed) return;
        try
        {
            // ConnectionStream 是热流；窗口可能在服务已经连接后才创建，必须先读取当前状态。
            ConnectionState = StateToText(_marketData.CurrentState);
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

    /// <summary>构造窗口标题显示名：合约名称 - 合约代码 + 期权后缀。</summary>
    private string BuildDisplayName()
        => FormatInstrumentDisplayName(InstrumentCode, _instrument, DateTime.Today);

    internal static string FormatInstrumentDisplayName(string instrumentCode, Instrument? instrument, DateTime today)
    {
        var suffix = instrument is { IsOptions: true } opt
            ? FormatOptionsSuffix(opt.ExpireDate, today)
            : string.Empty;
        var name = string.IsNullOrWhiteSpace(instrument?.Name)
            ? instrumentCode
            : $"{instrument.Name.Trim()} - {instrumentCode}";
        return string.IsNullOrEmpty(suffix) ? name : $"{name} {suffix}";
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
            MarshalToUi(() => ConnectionState = StateToText(_marketData.CurrentState), immediateIfNoDispatcher: true);
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
            RecordRelevantMarketUpdates(_lastMarketData, data, DateTime.Now);
            _lastMarketData = data;
            PriceLadder = data.ToPriceLadder(
                _priceTick, AskQuoteRowCount, BidQuoteRowCount, BuildPendingByPrice());
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
            PriceLadder = _lastMarketData.ToPriceLadder(
                _priceTick, AskQuoteRowCount, BidQuoteRowCount, BuildPendingByPrice());
        });
    }

    private void RecordRelevantMarketUpdates(DepthMarketData? previous, DepthMarketData current, DateTime observedAt)
    {
        if (previous is null || !SameDepth(previous.BidPrices, previous.BidVolumes, current.BidPrices, current.BidVolumes))
            Order.RecordMarketUpdate(PriceLadderTradeSide.FirstTradeColumn, observedAt);
        if (previous is null || !SameDepth(previous.AskPrices, previous.AskVolumes, current.AskPrices, current.AskVolumes))
            Order.RecordMarketUpdate(PriceLadderTradeSide.SecondTradeColumn, observedAt);
    }

    private static bool SameDepth(
        IReadOnlyList<decimal> leftPrices,
        IReadOnlyList<int> leftVolumes,
        IReadOnlyList<decimal> rightPrices,
        IReadOnlyList<int> rightVolumes) =>
        leftPrices.SequenceEqual(rightPrices) && leftVolumes.SequenceEqual(rightVolumes);

    /// <summary>
    /// 聚合当前活跃报单为「价格 → 数量」字典（按 PriceTick 对齐 key）。
    /// 同一价位上多笔挂单合并为一格显示数。
    /// </summary>
    internal IReadOnlyDictionary<decimal, int> BuildPendingByPrice()
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
            dict[alignedPrice] = dict.GetValueOrDefault(alignedPrice) + Math.Max(0, info.RemainingVolume);
        }
        return dict;
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
