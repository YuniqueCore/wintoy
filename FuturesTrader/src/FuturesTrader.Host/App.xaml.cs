using System.Windows;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.MarketData;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using FuturesTrader.Infrastructure.Persistence;
using FuturesTrader.Infrastructure.Persistence.WindowGroups;
using FuturesTrader.Infrastructure.Trading;
using FuturesTrader.Infrastructure.Trading.Ctp;
using FuturesTrader.Presentation.Abstractions;
using FuturesTrader.Presentation.Services;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.Views;
using FuturesTrader.Presentation.WindowHosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace FuturesTrader.Host;

/// <summary>
/// 应用入口：DI 装配 + Serilog 配置 + OnStartup 手动启动 MainWindow。
/// Host 是组合根：唯一同时引用 Application/Infrastructure/Presentation/Mcp 的层。
/// MCP HTTP 服务（默认关闭）：appsettings.json 的 Mcp:Enabled=true 时，
/// 在同一进程内启动 Kestrel，于 Mcp:Url 暴露 StreamableHTTP 端点（/mcp），
/// 供外部 agent 通过 HTTP 调用 ConfigTools 工具。关闭时零开销（不启动 web host）。
/// 注意：用完全限定 System.Windows.Application 与 Microsoft.Extensions.Hosting.Host，
/// 避免与当前命名空间 FuturesTrader.Host 冲突。
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost _host;

    public App()
    {
        // Serilog 配置：日志按日滚动到 logs/ 目录
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: "logs/app-.log",
                rollingInterval: RollingInterval.Day,
                encoding: System.Text.Encoding.UTF8)
            .CreateLogger();

        // 早读 MCP 开关：ConfigureWebHostDefaults 必须在 builder 配置阶段决策，
        // 故先独立加载 appsettings.json 取 Mcp:Enabled / Mcp:Url。
        var earlyConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var mcpEnabled = earlyConfig.GetValue<bool>("Mcp:Enabled");
        var mcpUrl = earlyConfig.GetValue<string>("Mcp:Url") ?? "http://127.0.0.1:51800";

        // DI 组合根：CreateDefaultBuilder 默认从 GetCurrentDirectory() 加载 appsettings.json，
        // 但 exe 可能从任意目录被启动（如资源管理器双击、计划任务），此时 cwd ≠ exe 目录会找不到配置。
        // 显式 UseContentRoot(AppContext.BaseDirectory) 让配置/日志始终相对 exe 目录解析，
        // 与上面 earlyConfig 的 SetBasePath(AppContext.BaseDirectory) 保持一致。
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog(Log.Logger, dispose: true)
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<ConfigFileOptions>(ctx.Configuration.GetSection("ConfigFile"));
                services.AddSingleton<IConfigRepository, ConfigRepository>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();

                // 窗口分组管理：仓库(Users.xml+window-groups.json) + 窗口宿主 + 编排服务 + 段 VM
                services.Configure<WindowLayoutOptions>(ctx.Configuration.GetSection("WindowLayout"));
                services.AddSingleton<IWindowGroupRepository, UsersXmlWindowGroupRepository>();
                // IWindowHost 真实实现：WindowManager 用 TradingWindow 替换 StubWindowHost 占位
                services.AddSingleton<IWindowHost, WindowManager>();
                services.AddSingleton<WindowGroupService>();
                services.AddSingleton<WindowGroupBarViewModel>();

                // 行情双源：MarketDataOptions.Provider 决定装配 Mock 或 Ctp（工厂模式）
                services.Configure<MarketDataOptions>(ctx.Configuration.GetSection("MarketData"));
                services.AddSingleton<IMarketDataService>(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    if (opts.Provider == MarketDataProvider.Ctp)
                    {
                        // CTP 直连：thostmduserapi_se.dll（32 位，需 Host 进程 x86）
                        Log.Information("CTP 行情实现启用：Front={Front} Flow={Flow}",
                            opts.FrontAddress, opts.FlowPath);
                        return new CtpMarketDataService(
                            opts,
                            loggerFactory.CreateLogger<CtpMarketDataService>());
                    }
                    return new SimulatedMarketDataService(
                        opts.MockTickIntervalMs,
                        loggerFactory.CreateLogger<SimulatedMarketDataService>());
                });

                // 交易双源：TradingOptions.Provider 决定装配 Mock 或 Ctp（与行情对称）。
                // CTP 模式直连 thosttraderapi_se.dll，需认证 BrokerID/UserID/Password/AppID/AuthCode。
                services.Configure<TradingOptions>(ctx.Configuration.GetSection("Trading"));
                services.AddSingleton<ITradingService>(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<TradingOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    if (opts.Provider == TradingProvider.Ctp)
                    {
                        Log.Information("CTP 交易实现启用：Front={Front} Flow={Flow} Broker={Broker} User={User}",
                            opts.FrontAddress, opts.FlowPath, opts.BrokerId, opts.UserId);
                        return new CtpTradingService(
                            opts,
                            loggerFactory.CreateLogger<CtpTradingService>());
                    }
                    Log.Information("Mock 交易实现启用（离线模拟报单/撤单/成交）");
                    return new MockTradingService(
                        loggerFactory.CreateLogger<MockTradingService>());
                });

                // 本地风控单例：从 config.ini [Order] 段加载 OrderConfig 构造 LocalRiskService。
                // 全会话共享一份计数器（撤单/报单限额按交易日计，不按合约隔离）。
                services.AddSingleton<ILocalRiskService>(sp =>
                {
                    var configRepo = sp.GetRequiredService<IConfigRepository>();
                    var configOpts = sp.GetRequiredService<IOptions<ConfigFileOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    try
                    {
                        var cloud = configRepo.Load(configOpts.Path);
                        Log.Information("本地风控已加载：RiskOpen={Risk} MaxInput={Input} MaxPos={Pos} GZ={Gz} SP={Sp} QQ={Qq}",
                            cloud.Order.RiskOpen, cloud.Order.MaxInputCount, cloud.Order.MaxPositionCount,
                            cloud.Order.MaxCancelGz, cloud.Order.MaxCancelSp, cloud.Order.MaxCancelQq);
                        return new LocalRiskService(
                            cloud.Order,
                            loggerFactory.CreateLogger<LocalRiskService>());
                    }
                    catch (Exception ex)
                    {
                        // config.ini 缺失或解析失败时退回默认配置（风控关闭），不阻断启动
                        Log.Warning(ex, "加载 config.ini Order 段失败，本地风控使用默认配置（关闭）");
                        return new LocalRiskService(
                            new Domain.Configuration.OrderConfig(),
                            loggerFactory.CreateLogger<LocalRiskService>());
                    }
                });

                // 声音/键盘单例：CTP 回调触发 Play/快捷键集中派发
                services.Configure<SoundOptions>(ctx.Configuration.GetSection("Sound"));
                services.AddSingleton<ISoundService, SoundService>();
                services.AddSingleton<IKeyboardOperationService, KeyboardOperationService>();

                // 下单校验链（对齐 0527.exe sub_4C036C 7 步校验）：
                // ITradingSessionChecker / ISpreadCalculator 纯逻辑无状态 → 单例；
                // IOrderValidator 含每窗口 CBNearby 点击节流状态 → Transient，让每个合约窗口独立节流。
                services.AddSingleton<ITradingSessionChecker, TradingSessionChecker>();
                services.AddSingleton<ISpreadCalculator, SpreadCalculator>();
                services.AddTransient<IOrderValidator, OrderValidator>();

                // MCP 工具+HTTP 传输注册（仅启用时；MapMcp 端点在下方 ConfigureWebHostDefaults 配置）
                if (mcpEnabled)
                {
                    services.AddMcpServer()
                        .WithHttpTransport(o => o.Stateless = true)
                        .WithFuturesTraderConfigTools();
                }
            });

        if (mcpEnabled)
        {
            // 同进程内嵌 Kestrel：监听 127.0.0.1，MapMcp 暴露 /mcp 端点。
            // StartAsync 后台启动，WPF 主线程继续渲染 UI。
            builder.ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(mcpUrl);
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        // 健康检查端点：agent/运维可 GET /ping 探活，确认 MCP 进程存活
                        endpoints.MapGet("/ping", () => "pong");
                        endpoints.MapMcp("/mcp");
                    });
                });
            });
            Log.Information("MCP 服务已启用，监听 {Url}（POST /mcp）", mcpUrl);
        }
        else
        {
            Log.Information("MCP 服务未启用（appsettings.json Mcp:Enabled=false）");
        }

        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();
        _host.Services.GetRequiredService<MainWindow>().Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
