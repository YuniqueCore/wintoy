# 06 - WPF 重构指南

> 本文整合前 5 篇文档的逆向成果，给出从 Delphi 0527.exe 升级到 WPF + C# 的完整重构方案。
> 核心目标：**保留原功能与操作习惯** + **架构现代化** + **可测试可演进**。
> 遵循 AGENTS.md §6 重构策略（分层架构 / 组件化 / 强类型 / 纯逻辑 / 可测试）。

## 1. 结论先行

- **技术栈**：.NET 8 + WPF + Syncfusion WPF（已购）+ CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting。
- **架构**：严格三层（UI / Domain / Infrastructure）+ 依赖倒转，CTP/HTTP/SQLite 全部抽象为接口。
- **CTP 接入**：**直接对接 CTP 官方 C# 绑定**（弃用 YYXX.dll），通过 P/Invoke 调用 `thosttraderapi_se.dll` / `thostmduserapi_se.dll`。
- **分阶段交付**：5 个里程碑（M1 架构骨架 → M2 行情 → M3 交易 → M4 配置/云端 → M5 UI 打磨），每个里程碑可独立验证。
- **数据兼容**：保留 GBK `config.ini` / `Users.xml` / `HQAddress.xml` / `Instruments.xml` / `PnL/*.csv` 的读写能力（提供迁移工具转 JSON）。
- **不做向后兼容**：旧 .con 状态文件、yy.dll/yyVolume.dll/YYXX.dll 等过渡产物全部废弃。

## 2. 目标技术栈

| 层 | 技术 | 理由 |
|---|---|---|
| 运行时 | .NET 8 (LTS) | 长期支持，性能优于 .NET Framework |
| UI 框架 | WPF + XAML | 用户指定，Windows 桌面首选 |
| MVVM 框架 | CommunityToolkit.Mvvm | 微软官方，源生成器减少样板 |
| UI 控件库 | Syncfusion WPF Suite | 用户已有，覆盖 DataGrid/Chart/UpDown 等 |
| 图标库 | Material Design Icons (NuGet: `MaterialDesignThemes`) | 专业图标，无 emoji |
| DI / 主机 | Microsoft.Extensions.Hosting | 通用主机，统一生命周期管理 |
| 日志 | Serilog + Serilog.Sinks.File + Seq | 结构化日志 |
| HTTP | System.Net.Http.HttpClient + Polly | 现代化 + 重试策略 |
| JSON | System.Text.Json | 内置高性能 |
| 配置 | Microsoft.Extensions.Configuration | 多源（JSON/ENV/CLI） |
| SQLite | Microsoft.Data.Sqlite + Dapper | 轻量，无需 EF Core |
| CTP 绑定 | 自研 P/Invoke 包装器（基于官方 C++ 头文件） | 直接对接 CTP，绕开 YYXX.dll |
| 单元测试 | xUnit + FluentAssertions + Moq | .NET 主流 |
| UI 测试 | FlaUI 或 Appium.Windows | 自动化 UI 测试 |

## 3. 架构设计（分层 + 依赖倒转）

### 3.1 项目结构

```
FuturesTrader.sln
├── src/
│   ├── FuturesTrader.Domain/              # 领域层（纯 C#，无依赖）
│   │   ├── Trading/
│   │   │   ├── Order.cs                    # 订单值对象
│   │   │   ├── Trade.cs                    # 成交值对象
│   │   │   ├── Position.cs                 # 持仓值对象
│   │   │   ├── OrderSide.cs                # 强类型枚举
│   │   │   └── ITradingService.cs          # 交易接口
│   │   ├── MarketData/
│   │   │   ├── Instrument.cs               # 合约值对象
│   │   │   ├── DepthMarketData.cs          # 深度行情
│   │   │   ├── TickType.cs                 # Tick 类型枚举
│   │   │   └── IMarketDataService.cs       # 行情接口
│   │   ├── Account/
│   │   │   ├── TradingAccount.cs           # 资金账户
│   │   │   └── IAccountService.cs          # 账户接口
│   │   ├── Cloud/
│   │   │   ├── CloudConfig.cs              # 云端配置
│   │   │   ├── ServerEndpoint.cs           # 服务器端点
│   │   │   ├── RemoteCommand.cs            # 远程命令
│   │   │   └── ICloudService.cs            # 云端接口
│   │   └── Policies/
│   │       ├── OpenAuctionPolicy.cs        # 开盘抢单策略
│   │       ├── StopLossPolicy.cs           # 止损策略
│   │       └── RiskControlPolicy.cs        # 风控策略
│   │
│   ├── FuturesTrader.Infrastructure/      # 基础设施层
│   │   ├── Ctp/
│   │   │   ├── Native/                     # P/Invoke 定义
│   │   │   │   ├── ThostTraderApiNative.cs
│   │   │   │   ├── ThostMdApiNative.cs
│   │   │   │   └── Structs/                # CTP 结构体映射
│   │   │   ├── CtpTradingService.cs        # ITradingService 实现
│   │   │   ├── CtpMarketDataService.cs     # IMarketDataService 实现
│   │   │   ├── CtpAccountService.cs        # IAccountService 实现
│   │   │   └── CtpCallbackRouter.cs        # SPI 回调分发
│   │   ├── Cloud/
│   │   │   ├── HttpCloudService.cs         # ICloudService 实现
│   │   │   ├── CloudFileDownloader.cs      # 文件下载器
│   │   │   └── RemoteCommandPoller.cs      # 远程命令轮询
│   │   ├── Persistence/
│   │   │   ├── SqliteMarketDataStore.cs    # 行情数据存储
│   │   │   ├── ConfigRepository.cs         # config.ini 读写（GBK）
│   │   │   ├── UserRepository.cs           # Users.xml 读写
│   │   │   └── InstrumentsRepository.cs    # Instruments.xml 读写
│   │   └── Audio/
│   │       └── SoundPlayer.cs              # 音频播放
│   │
│   ├── FuturesTrader.Application/          # 应用层（编排）
│   │   ├── TradingOrchestrator.cs          # 交易编排
│   │   ├── LoginFlow.cs                    # 登录流程
│   │   ├── OpenAuctionScheduler.cs         # 开盘抢单调度
│   │   └── StateMachines/
│   │       ├── ConnectionState.cs           # 连接状态机
│   │       └── OrderState.cs               # 订单状态机
│   │
│   ├── FuturesTrader.Presentation/         # UI 表现层（WPF）
│   │   ├── Views/
│   │   │   ├── MainWindow.xaml
│   │   │   ├── TradingView.xaml            # ← TYYWin
│   │   │   ├── InstrumentsView.xaml        # ← TInstrumentsWin
│   │   │   ├── MonitoredView.xaml          # ← TMonitoredWin
│   │   │   ├── UserEditView.xaml           # ← TUserEdit
│   │   │   ├── OrderControlPanel.xaml      # ← TYYCtrlWin
│   │   │   ├── PointManageView.xaml        # ← TPointWindow
│   │   │   ├── SoundConfigView.xaml        # ← TSoundWin
│   │   │   ├── CloseRecordView.xaml        # ← TCloseRecordWin
│   │   │   ├── SpreadRecordView.xaml       # ← TJCJLWin
│   │   │   └── PositionView.xaml           # ← PositionWin（嵌入）
│   │   ├── ViewModels/
│   │   │   └── ...（一一对应）
│   │   ├── Controls/
│   │   │   ├── PriceListControl.xaml       # ← 价差居中买卖盘
│   │   │   ├── CtrBySpreadControl.xaml     # ← TXCntrbySprd* 族
│   │   │   └── DepthMarketDataGrid.xaml
│   │   ├── Converters/
│   │   ├── Services/
│   │   │   ├── IDialogService.cs
│   │   │   ├── INotificationService.cs
│   │   │   └── IWindowManager.cs
│   │   └── App.xaml
│   │
│   └── FuturesTrader.Host/                 # 主机层（启动）
│       ├── Program.cs                      # Main 入口
│       ├── appsettings.json                # 配置
│       └── FuturesTrader.Host.csproj
│
├── tests/
│   ├── FuturesTrader.Domain.Tests/         # 领域层单元测试
│   ├── FuturesTrader.Application.Tests/    # 应用层测试
│   ├── FuturesTrader.Infrastructure.Tests/ # 基础设施层测试（mock CTP/HTTP）
│   └── FuturesTrader.Ui.Tests/             # UI 自动化测试
│
└── tools/
    ├── CtpStructGenerator/                 # 从 CTP 头文件生成 C# 结构体
    ├── ConfigMigrator/                     # config.ini (GBK) → JSON
    └── UsersMigrator/                      # Users.xml → JSON
```

### 3.2 依赖方向（强制单向）

```
Presentation ──→ Application ──→ Domain ←── Infrastructure
                                              ↑
                                  （实现 Domain 接口）
```

- **Domain** 是核心，**不依赖**任何外部包（无 WPF、无 HTTP、无 SQLite）。
- **Infrastructure** 实现 Domain 接口，可被替换（CTP → 模拟；HTTP → 内存）。
- **Presentation** 只观察 Domain 状态、表达用户意图，**不含业务规则**。
- **Application** 编排 Domain 与 Infrastructure，处理事务/调度。

### 3.3 DI 注册示例

```csharp
// Program.cs
var builder = Host.CreateApplicationBuilder(args);

// Domain services (singleton, stateful)
builder.Services.AddSingleton<ITradingService, CtpTradingService>();
builder.Services.AddSingleton<IMarketDataService, CtpMarketDataService>();
builder.Services.AddSingleton<IAccountService, CtpAccountService>();
builder.Services.AddSingleton<ICloudService, HttpCloudService>();

// Application orchestrators
builder.Services.AddSingleton<TradingOrchestrator>();
builder.Services.AddSingleton<LoginFlow>();
builder.Services.AddSingleton<OpenAuctionScheduler>();

// Infrastructure
builder.Services.AddSingleton<SqliteMarketDataStore>();
builder.Services.AddSingleton<ConfigRepository>();
builder.Services.AddSingleton<UserRepository>();
builder.Services.AddSingleton<IHttpClientProvider, TypedHttpClientProvider>();

// UI
builder.Services.AddSingleton<MainWindow>();
builder.Services.AddSingleton<TradingViewModel>();
// ... 其他 ViewModel

var host = builder.Build();
host.Start();
```

## 4. 关键接口设计

### 4.1 交易接口（替换 YYXX.dll 15 个导出）

```csharp
public interface ITradingService {
    // 生命周期
    Task<ConnectionState> ConnectAsync(TradeServerEndpoint endpoint, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
    Task<LoginResult> LoginAsync(LoginRequest req, CancellationToken ct);
    Task<AuthResult> AuthenticateAsync(AuthRequest req, CancellationToken ct);
    Task ConfirmSettlementAsync(CancellationToken ct);

    // 交易
    Task<OrderResult> OrderInsertAsync(OrderRequest req, CancellationToken ct);
    Task<OrderResult> OrderActionAsync(ActionRequest req, CancellationToken ct);

    // 查询
    Task<IReadOnlyList<Instrument>> QueryInstrumentsAsync(CancellationToken ct);
    Task<IReadOnlyList<Order>> QueryOrdersAsync(CancellationToken ct);
    Task<IReadOnlyList<Trade>> QueryTradesAsync(CancellationToken ct);
    Task<TradingAccount> QueryTradingAccountAsync(CancellationToken ct);
    Task<IReadOnlyList<Position>> QueryPositionsAsync(CancellationToken ct);
    Task<IReadOnlyList<PositionDetail>> QueryPositionDetailsAsync(CancellationToken ct);

    // 事件流（替代 SPI 回调）
    IObservable<Order> OrderStream { get; }             // OnRtnOrder
    IObservable<Trade> TradeStream { get; }             // OnRtnTrade
    IObservable<ErrorInfo> ErrorStream { get; }         // OnRspError / OnErrRtn*
    IObservable<ConnectionState> ConnectionStream { get; }

    string TradingDay { get; }                          // GetTradingDay
}
```

### 4.2 行情接口

```csharp
public interface IMarketDataService {
    Task<ConnectionState> ConnectAsync(MarketServerEndpoint endpoint, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);

    Task SubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken ct);
    Task UnsubscribeAsync(IReadOnlyCollection<string> instrumentIds, CancellationToken ct);
    Task UnsubscribeAllAsync(CancellationToken ct);

    IObservable<DepthMarketData> MarketDataStream { get; }  // OnRtnDepthMarketData
    IObservable<ConnectionState> ConnectionStream { get; }
}

public sealed record DepthMarketData(
    string InstrumentId,
    string TradingDay,
    decimal LastPrice,
    decimal PreSettlementPrice,
    decimal OpenPrice,
    decimal HighestPrice,
    decimal LowestPrice,
    decimal Volume,
    decimal Turnover,
    decimal OpenInterest,
    decimal UpperLimitPrice,
    decimal LowerLimitPrice,
    TimeOnly UpdateTime,
    int UpdateMillisec,
    IReadOnlyList<PriceVolume> Bids,    // 5 档买盘
    IReadOnlyList<PriceVolume> Asks,    // 5 档卖盘
    decimal AveragePrice
);

public sealed record PriceVolume(decimal Price, int Volume);
```

### 4.3 云端接口

（详见 [04-http-cloud.md](04-http-cloud.md) §7.1）

### 4.4 状态机（替代零散 bool）

```csharp
// 连接状态机（替代 isLoading / isConnected 等布尔）
public abstract record ConnectionState {
    public sealed record Disconnected : ConnectionState;
    public sealed record Connecting : ConnectionState;
    public sealed record Authenticating : ConnectionState;
    public sealed record LoggingIn : ConnectionState;
    public sealed record Connected(string TradingDay, long SessionId) : ConnectionState;
    public sealed record Reconnecting(int Attempt, TimeSpan NextRetry) : ConnectionState;
    public sealed record Failed(string Error) : ConnectionState;
}

// 订单状态机（与 CTP OrderStatus 对齐）
public abstract record OrderState {
    public sealed record Pending : OrderState;                          // Unknown
    public sealed record Accepted(string OrderSysID) : OrderState;       // Accepted
    public sealed record PartiallyFilled(int Filled, int Remaining) : OrderState;
    public sealed record FullyFilled(int Filled) : OrderState;
    public sealed record Cancelled(string CancelTime) : OrderState;
    public sealed record Rejected(string Error) : OrderState;
}
```

### 4.5 强类型值对象

```csharp
// 替代裸 string/decimal
public readonly record struct InstrumentId(string Value) {
    public string ProductId => /* 解析规则 */;
    public bool IsOption => Value.Contains('C') || Value.Contains('P');
    public static InstrumentId Parse(string s) => new(s);
}

public readonly record struct OrderRef(int Value) {
    public static OrderRef Next(OrderRef current) => new(current.Value + 1);
}

public readonly record struct Price(decimal Value, int TickSize) {
    public Price NextUp() => new(Value + TickSize, TickSize);
    public Price NextDown() => new(Value - TickSize, TickSize);
}

public enum OrderSide { Buy, Sell }
public enum OffsetFlag { Open, Close, CloseToday, CloseYesterday }
public enum HedgeFlag { Speculation, Arbitrage, Hedge }
```

## 5. CTP 直连方案（弃用 YYXX.dll）

### 5.1 P/Invoke 包装

直接对接 CTP 官方 C++ API，无需 YYXX.dll 中间层：

```csharp
// ThostTraderApiNative.cs
internal static class ThostTraderApiNative {
    private const string Dll = "thosttraderapi_se.dll";

    [DllImport(Dll, EntryPoint = "?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N1_N@Z",
               CallingConvention = CallingConvention.ThisCall)]
    public static extern IntPtr CreateFtdcTraderApi(string flowPath, bool isUsingUdp, bool isMulticast);

    // RegisterSpi / RegisterFront / Init / Release / ReqUserLogin / ReqAuthenticate / ...
}

// CtpTradingService.cs
public sealed class CtpTradingService : ITradingService {
    private IntPtr _api;
    private readonly CtpCallbackRouter _router = new();

    public async Task<LoginResult> LoginAsync(LoginRequest req, CancellationToken ct) {
        // 1. CreateFtdcTraderApi
        // 2. RegisterSpi(_router)
        // 3. SubscribePublicTopic / SubscribePrivateTopic
        // 4. RegisterFront(endpoint.Address)
        // 5. Init()
        // 6. await _router.WaitForConnectionAsync(ct)
        // 7. ReqAuthenticate(req.AppID, req.AuthCode)
        // 8. await _router.WaitForAuthAsync(ct)
        // 9. ReqUserLogin(req.UserId, req.Password)
        // 10. await _router.WaitForLoginAsync(ct)
        // 11. ReqSettlementInfoConfirm()
    }
}
```

### 5.2 回调路由（C++ SPI → C# IObservable）

CTP 的 SPI 是 C++ vtable 回调，需要 C++/CLI 或 delegate 桥接：

```csharp
internal sealed class CtpCallbackRouter : CThostFtdcTraderSpi {
    private readonly Subject<Order> _orderSubject = new();
    private readonly Subject<Trade> _tradeSubject = new();
    private readonly TaskCompletionSource<LoginResult> _loginTcs = new();

    public IObservable<Order> OrderStream => _orderSubject;
    public IObservable<Trade> TradeStream => _tradeSubject;

    public override void OnRtnOrder(ref CThostFtdcOrderField field) {
        var order = MapToDomain(ref field);
        _orderSubject.OnNext(order);
    }

    public override void OnRtnTrade(ref CThostFtdcTradeField field) {
        var trade = MapToDomain(ref field);
        _tradeSubject.OnNext(trade);
    }

    public override void OnRspUserLogin(ref CThostFtdcRspUserLoginField field,
                                         ref CThostFtdcRspInfoField info, bool isLast) {
        if (info.ErrorID == 0)
            _loginTcs.TrySetResult(MapToLoginResult(ref field));
        else
            _loginTcs.TrySetException(new CtpException(info.ErrorID, info.ErrorMsg));
    }
}
```

> **注**：CTP C++ API 的 C# 绑定可用现成开源项目如 `ctp-swift` / `OpenCTP-CSharp`，避免重复造轮子。需评估license 兼容性。

### 5.3 行情断线重连机制（保留原 5 秒重订阅）

```csharp
internal sealed class CtpMarketDataService : IMarketDataService {
    private readonly TimeSpan _reconnectInterval = TimeSpan.FromSeconds(5);

    public IObservable<ConnectionState> ConnectionStream =>
        _connectionSubject.AsObservable();

    private async Task MonitorConnectionAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            if (_connectionSubject.Value is Connected) {
                await Task.Delay(_reconnectInterval, ct);
                continue;
            }
            // 触发5秒 重新订阅（与原 SysLog 行为一致）
            _connectionSubject.OnNext(new Reconnecting(attempt++, _reconnectInterval));
            await ReconnectAndResubscribeAsync(ct);
        }
    }
}
```

## 6. 数据兼容与迁移

### 6.1 配置文件迁移

```csharp
// 读取旧 GBK config.ini
public sealed class ConfigRepository {
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);

    public CloudConfig Load(string path) {
        var lines = File.ReadAllLines(path, Gbk);
        return ParseIni(lines);  // [Window]/[Order]/[User] 三段
    }

    public void Save(string path, CloudConfig config) {
        var lines = SerializeIni(config);
        File.WriteAllLines(path, lines, Gbk);  // 保留 GBK 写入
    }
}
```

提供迁移工具 `tools/ConfigMigrator`：将 GBK `config.ini` 转为 `appsettings.json`，支持回滚。

### 6.2 用户数据迁移

- `Users.xml` → 保留 XML 格式（窗口布局字段直接绑定）+ 可选迁移到 `users.json`
- `HQAddress.xml` / `JYAddress.xml` → 合并为 `servers.json`
- `Instruments.xml` → 改为运行时通过 CTP `QryInstrument` 查询 + 本地 SQLite 缓存（增量更新）

### 6.3 SQLite 行情存储

```csharp
public sealed class SqliteMarketDataStore {
    private readonly SqliteConnection _conn;

    public SqliteMarketDataStore(string dbPath) {
        _conn = new($"Data Source={dbPath}");
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema() {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS market_data (
                id            INTEGER PRIMARY KEY,
                instrument_id TEXT NOT NULL,
                ts            INTEGER NOT NULL,
                tp            INTEGER NOT NULL,
                data          TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_market_data_lookup
                ON market_data(instrument_id, ts DESC);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Insert(DepthMarketData md) { /* ... */ }
    public IReadOnlyList<DepthMarketData> Query(string instrumentId, TimeRange range) { /* ... */ }
}
```

> 表结构保留与原一致（[03-data-formats.md](03-data-formats.md) §9），新增索引优化查询。

### 6.4 PnL CSV

保留 GBK CSV 格式（Excel 兼容），通过 `CsvHelper` + GBK 编码写入。每日一份文件 `PnL/YYYYMMDD.csv`。

## 7. UI 重构（保留操作习惯）

详见 [05-ui-windows.md](05-ui-windows.md) §7–§8。核心要点：

1. **价差居中控件族** → `CtrBySpreadControl` 自定义 UserControl
2. **TStringGrid** → `Syncfusion.SfDataGrid` + 自定义模板
3. **多窗口** → `SfDockingManager` + 布局持久化到 Users.xml
4. **托盘** → `H.NotifyIcon.Wpf`
5. **滚轮加速** → 自定义 `ScrollViewer.PreviewMouseWheel`
6. **字体** → 默认保留 `新宋体`，可选切换
7. **DPI** → PerMonitorV2
8. **图标** → Material Design Icons（禁止 emoji）

## 8. 业务逻辑复刻

### 8.1 开盘抢单调度

```csharp
public sealed class OpenAuctionScheduler {
    private readonly ITradingService _trading;
    private readonly TimeProvider _clock;
    private readonly OpenAuctionConfig _config;  // MOrderTime1-9, MOrderXSpeed, MOrderXStop

    public async Task RunAsync(CancellationToken ct) {
        foreach (var triggerTime in _config.TriggerTimes) {
            await WaitUntilAsync(triggerTime, ct);
            await ExecuteAuctionAsync(ct);
        }
    }

    private async Task ExecuteAuctionAsync(CancellationToken ct) {
        var deadline = _clock.GetUtcNow().AddMilliseconds(_config.XStop);
        while (_clock.GetUtcNow() < deadline) {
            await _trading.OrderInsertAsync(BuildOrder(), ct);
            await Task.Delay(_config.XSpeed, ct);  // 200ms 间隔
        }
    }
}
```

### 8.2 风控策略

```csharp
public sealed class RiskControlPolicy {
    private readonly RiskConfig _config;  // MaxCancelGZ=395, MaxCancelSP=10000, MaxCancelQQ=10000

    public RiskCheckResult Check(OrderRequest req, RiskContext ctx) {
        if (ExceedsCancelLimit(ctx, out var reason))
            return RiskCheckResult.Rejected(reason);

        if (_config.RiskOpen && ExceedsMaxPosition(ctx, req))
            return RiskCheckResult.Rejected("超过最大持仓限制");

        if (_config.CloudRiskOn && !await CloudRiskApproveAsync(req))
            return RiskCheckResult.Rejected("云风控拒绝");

        return RiskCheckResult.Approved;
    }
}
```

### 8.3 远程命令处理

```csharp
public sealed class RemoteCommandPoller {
    private readonly ICloudService _cloud;
    private readonly ICommandDispatcher _dispatcher;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            try {
                var cmd = await _cloud.PollRemoteCommandAsync(_userId, ct);
                if (cmd is not null)
                    await _dispatcher.DispatchAsync(cmd, ct);
            } catch (Exception ex) {
                _log.LogWarning(ex, "Remote poll failed");
            }
            await Task.Delay(_interval, ct);
        }
    }
}
```

> 原软件的 `TRemoteThread` 改为 `RemoteCommandPoller`。**新增**：用户可见的命令日志 + 可关闭开关。

## 9. 测试策略

### 9.1 单元测试（xUnit）

```csharp
public class OrderStateTests {
    [Fact]
    public void Pending_Accepted_PartiallyFilled_FullyFilled_transition_chain() {
        var state = OrderState.Pending;
        state = state.Transition(OrderEvent.Accepted("SYS001"));
        state.Should().BeOfType<OrderState.Accepted>();
        // ...
    }
}

public class RiskControlPolicyTests {
    [Theory]
    [InlineData(InstrumentKind.GZ, 395, true)]    // 股指 395 触发
    [InlineData(InstrumentKind.SP, 10000, true)]  // 商品 10000 触发
    [InlineData(InstrumentKind.QQ, 10000, true)]  // 期权 10000 触发
    public void Rejects_when_cancel_limit_exceeded(InstrumentKind kind, int cancels, bool expected) {
        var policy = new RiskControlPolicy(new RiskConfig { MaxCancelGZ = 395, MaxCancelSP = 10000, MaxCancelQQ = 10000 });
        var ctx = new RiskContext { CancelCount = cancels, Kind = kind };
        var result = policy.Check(BuildOrder(), ctx);
        result.IsRejected.Should().Be(expected);
    }
}
```

### 9.2 集成测试（mock CTP）

```csharp
public class CtpTradingServiceTests {
    private readonly MockCtpApi _mockApi = new();
    private readonly CtpTradingService _service;

    public CtpTradingServiceTests() {
        _service = new CtpTradingService(_mockApi);
    }

    [Fact]
    public async Task Login_completes_authenticate_login_settlement_confirm_flow() {
        _mockApi.SetupAuth().SetupLogin("20260724", sessionId: -367078502);

        var result = await _service.LoginAsync(new LoginRequest("338897", "..."), default);

        result.TradingDay.Should().Be("20260724");
        result.SessionId.Should().Be(-367078502);
        _mockApi.Received.SettlementInfoConfirmCalls.Should().Be(1);
    }
}
```

### 9.3 UI 测试（FlaUI）

```csharp
public class TradingViewTests {
    [Fact]
    public void Double_click_monitored_instrument_opens_trading_window() {
        using var app = Application.Launch("FuturesTrader.Host.exe");
        var window = app.GetMainWindow();
        var grid = window.FindFirstDescendant(cf => cf.ByAutomationId("MonitoredGrid"));
        var cell = grid.FindFirstDescendant(cf => cf.ByAutomationId("ag2608"));

        cell.DoubleClick();

        var tradingWindow = window.FindFirstDescendant(cf => cf.ByAutomationId("TradingView"));
        tradingWindow.Should().NotBeNull();
    }
}
```

## 10. 里程碑计划

### M1: 架构骨架 + 配置/数据迁移（1 周）

- 搭建解决方案结构（4 个项目 + 4 个测试项目）
- DI/Hosting/日志/配置基础
- 实现 `ConfigRepository` / `UserRepository` / `SqliteMarketDataStore`
- 迁移工具：`config.ini` ↔ JSON / `Users.xml` ↔ JSON
- 验证：能读旧 GBK 配置并加载到 `CloudConfig` 对象

### M2: 行情模块 + UI 骨架（2 周）

- 实现 `CtpMarketDataService`（P/Invoke `thostmduserapi_se.dll`）
- 实现 `CtpCallbackRouter` 行情回调路由
- 实现 `MainWindow` + `TradingView` XAML（Syncfusion SfDataGrid）
- 实现 `PriceListControl`（价差居中买卖盘）
- 实现 `InstrumentsView`（合约选择）
- 验证：能登录 SimNow 行情并订阅 30493 个合约显示在 UI

### M3: 交易模块 + 下单控制（2 周）

- 实现 `CtpTradingService`（P/Invoke `thosttraderapi_se.dll`）
- 实现登录认证全流程（`ReqAuthenticate` → `ReqUserLogin` → `ReqSettlementInfoConfirm`）
- 实现下单/撤单/查询全部接口
- 实现 `OrderControlPanel` / `PositionView`
- 实现 `OpenAuctionScheduler` 开盘抢单
- 实现 `RiskControlPolicy` 风控
- 验证：能在 SimNow 完成下单→成交→持仓更新全流程

### M4: 云端接口 + 业务编排（1 周）

- 实现 `HttpCloudService`（13 个 HTTP 端点，强制 HTTPS）
- 实现 `RemoteCommandPoller`（带用户可见日志 + 可关闭开关）
- 实现 `SoundPlayer`（音效触发）
- 实现 `CloseRecordView`（平仓记录 + PnL CSV 写入）
- 验证：能拉云端配置/合约/止损单并执行

### M5: UI 打磨 + 测试补全（1 周）

- 窗口布局持久化（`Users.xml` 的 `<WindowHistory>`）
- 系统托盘 + 右键菜单
- 字体配置 + DPI 适配
- 滚轮加速 + 键盘交互
- 全套单元测试 + UI 测试通过
- 性能测试（30493 合约订阅 + 行情刷新延迟 < 50ms）

**总工期：7 周**（单人开发估计；多人并行可压缩到 4-5 周）。

## 11. 风险与对策

| 风险 | 等级 | 对策 |
|---|---|---|
| CTP C# 绑定不稳定 | 高 | 评估开源 `OpenCTP-CSharp`；最坏情况 P/Invoke 现有 YYXX.dll 作为过渡 |
| VMProtect 加壳的 YYXX.dll 无法反编译 | 中 | 不依赖反编译，直接对接 CTP 原生 API |
| 高频行情下 UI 卡顿 | 中 | `DispatcherTimer` 节流 + `ObservableCollection` 批量更新 + 虚拟化 |
| 30493 合约订阅性能 | 中 | 增量订阅 + 优先级队列（自选优先） |
| 云端接口未抓包确认格式 | 高 | M4 启动前用 Fiddler 抓包验证 |
| 用户不接受新 UI | 高 | M5 严格按 [05-ui-windows.md](05-ui-windows.md) §8 习惯清单 1:1 复刻 |
| GBK 编码兼容 | 低 | `Encoding.GetEncoding(936)`，单元测试覆盖 |
| SimNow 环境不稳定 | 中 | 开发期可用 OpenCTP 模拟环境替代 |

## 12. 局限

- **CTP 接口签名**：依赖 CTP 6.7.10 官方 C++ 头文件，需通过 `tools/CtpStructGenerator` 从头文件生成 C# 结构体，避免手工抄写错误。
- **远程命令格式**：未抓包，`RemoteCommand` 类的字段需在 M4 阶段根据实际响应填充。
- **`UserContract.aspx` 双调用**：是 GET 还是 POST？是否带 body？需运行时确认。
- **`yiyikeji.top/jtqy.aspx` 业务语义**：未知，建议产品确认是否保留。
- **`TJCJLWin` 业务定位**：JCJL 缩写未确认，需 IDR 反编译或产品确认。

## 13. 反编译工具选择（针对原软件）

| 工具 | 用途 | 适用性 |
|---|---|---|
| **IDR (Interactive Delphi Reconstructor)** | 反编译 Delphi VCL，恢复类层次/方法/DFM | ★★★★★（首选） |
| **Resource Hacker** | 提取 DFM 资源、菜单、图标 | ★★★★ |
| **Ghidra / IDA Pro** | 通用反汇编 | ★★★（YYXX.dll 受 VMP 限制） |
| **PE Explorer** | 替代 Resource Hacker | ★★★ |
| ~~dnSpy / dnSpyEx~~ | .NET 反编译 | ❌ 不适用（本程序是 Delphi 原生） |

> 用户原文提到的 `dnspyex` 与 `https://github.com/dotnet/skills` 仅适用于 .NET 程序。经 [01-overview.md](01-overview.md) §1 确认，本程序是 **Delphi VCL 原生代码**，dnSpy/dnSpyEx 无法处理。
>
> `https://github.com/AgentSmithers/DnSpy-MCPserver-Extension` 同样基于 dnSpy，亦不适用。建议改用 **IDR** 深度反编译。

## 14. 相关文档

- [01-overview.md](01-overview.md) — 软件总览
- [02-ctp-api.md](02-ctp-api.md) — CTP 交易/行情接口
- [03-data-formats.md](03-data-formats.md) — 配置与数据文件格式
- [04-http-cloud.md](04-http-cloud.md) — HTTP 云端接口
- [05-ui-windows.md](05-ui-windows.md) — UI 窗体与功能映射

## 15. 工具调用简报

| 工具 | 触发原因 | 关键参数 | 结果概览 |
|---|---|---|---|
| PowerShell `Get-ChildItem` | 列出 docs/ 已有文件 | `D:\work\projs\futures\docs\` | 确认已有 01/02/03 三份文档 |
| Read `strings-ascii-urls.txt` | 提取 HTTP URL | 14 条 URL | 13 个 HTTP 端点全部命中 |
| Read `dfm-controls-summary.txt` | 提取窗体控件 | 11 个窗体控件清单 | 完成窗体→功能映射 |
| Read `dfm-forms-0527.txt` | 验证 DFM 结构 | TMonitoredWin 实证 | SGMonitoredInstrument/SGMonitoredProduct 控件确认 |
| Read `02-ctp-api.md` | 复用 CTP 接口信息 | 15 导出 + 9 回调 | 06 §4 接口设计直接引用 |
| Read `03-data-formats.md` | 复用数据格式 | config.ini/Users.xml 等 | 06 §6 数据兼容直接引用 |
| Write `04-http-cloud.md` | 创建 HTTP 接口文档 | 13 端点 + WPF 映射 | 完成 |
| Write `05-ui-windows.md` | 创建 UI 窗体文档 | 11 窗体 + WPF 映射 | 完成 |
| Write `06-refactor-guide.md` | 创建重构指南 | 5 里程碑 + 测试策略 | 完成 |
