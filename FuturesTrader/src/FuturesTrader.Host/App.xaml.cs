using System.Windows;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.MarketData;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using FuturesTrader.Infrastructure.Network;
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
using Wpf.Ui.Appearance;

namespace FuturesTrader.Host;

/// <summary>
/// 应用入口：单例守卫 → 主题 → 登录页 → 登录成功 → 浮动工具栏。
/// Host 是组合根：唯一同时引用 Application/Infrastructure/Presentation/Mcp 的层。
/// <para>
/// 启动流程：
/// <list type="number">
///   <item><see cref="SingleInstanceGuard.TryAcquire"/> 拦截多开（已运行则激活前台并退出）</item>
///   <item><see cref="ThemeService"/> 应用持久化主题</item>
///   <item>显示 <see cref="LoginWindow"/>（多行情/多账号/测速/密码登录）</item>
///   <item>登录成功 → <see cref="FloatingMainWindow"/> 显示 + 关闭登录页</item>
/// </list>
/// </para>
/// <para>
/// MCP HTTP 服务（默认关闭）：appsettings.json 的 Mcp:Enabled=true 时在同一进程内启动 Kestrel，
/// 于 Mcp:Url 暴露 StreamableHTTP 端点（/mcp）。
/// </para>
/// </summary>
public partial class App : System.Windows.Application
{
    private readonly IHost _host;
    private SingleInstanceGuard? _singleInstance;

    public App()
    {
        // 日志路径绝对化：相对路径基于 exe 目录（AppContext.BaseDirectory），避免工作目录不同导致日志丢失
        var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "logs", "app-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                encoding: System.Text.Encoding.UTF8)
            .CreateLogger();

        var earlyConfig = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();
        var mcpEnabled = earlyConfig.GetValue<bool>("Mcp:Enabled");
        var mcpUrl = earlyConfig.GetValue<string>("Mcp:Url") ?? "http://127.0.0.1:51800";

        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .UseSerilog(Log.Logger, dispose: true)
            .ConfigureServices((ctx, services) =>
            {
                // ── 配置选项绑定 ──
                services.Configure<ConfigFileOptions>(ctx.Configuration.GetSection("ConfigFile"));
                services.Configure<WindowLayoutOptions>(ctx.Configuration.GetSection("WindowLayout"));
                services.Configure<MarketDataOptions>(ctx.Configuration.GetSection("MarketData"));
                services.Configure<TradingOptions>(ctx.Configuration.GetSection("Trading"));
                services.Configure<LoginOptions>(ctx.Configuration.GetSection("Login"));
                services.Configure<UiOptions>(ctx.Configuration.GetSection("Ui"));
                services.Configure<SoundOptions>(ctx.Configuration.GetSection("Sound"));

                // ── 路径绝对化 PostConfigure：将 appsettings.json 中的相对路径基于 exe 目录解析为绝对路径。
                //    确保无论从哪个工作目录启动（双击/计划任务/命令行），配置/数据/音效/流文件路径都能正确解析。
                services.PostConfigure<ConfigFileOptions>(o => o.Path = ResolvePath(o.Path));
                services.PostConfigure<WindowLayoutOptions>(o =>
                {
                    o.UsersXmlPath = ResolvePath(o.UsersXmlPath);
                    o.GroupsJsonPath = ResolvePath(o.GroupsJsonPath);
                });
                services.PostConfigure<LoginOptions>(o =>
                {
                    o.HqAddressXmlPath = ResolvePath(o.HqAddressXmlPath);
                    o.UsersXmlPath = ResolvePath(o.UsersXmlPath);
                    o.ConfigIniPath = ResolvePath(o.ConfigIniPath);
                });
                services.PostConfigure<SoundOptions>(o => o.BasePath = ResolvePath(o.BasePath));
                services.PostConfigure<MarketDataOptions>(o => o.FlowPath = ResolvePath(o.FlowPath));
                services.PostConfigure<TradingOptions>(o => o.FlowPath = ResolvePath(o.FlowPath));

                // ── 配置 / 持久化仓库 ──
                services.AddSingleton<IConfigRepository, ConfigRepository>();
                services.AddSingleton<IWindowGroupRepository, UsersXmlWindowGroupRepository>();
                services.AddSingleton<IHqAddressRepository, HqAddressXmlRepository>();
                services.AddSingleton<IAccountRepository, UsersXmlAccountRepository>();

                // ── 网络测速 ──
                services.AddSingleton<IConnectionProbeService, TcpConnectionProbeService>();

                // ── 主题服务 ──
                services.AddSingleton<IThemeService, ThemeService>();

                // ── 行情/交易服务工厂（按 Provider 选型，登录后由 SessionService 调用）──
                services.AddSingleton<IMarketDataServiceFactory>(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<MarketDataOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    return opts.Provider == MarketDataProvider.Ctp
                        ? new CtpMarketDataServiceFactory(loggerFactory, opts.PriceLadderLevels)
                        : new SimulatedMarketDataServiceFactory(loggerFactory, opts.MockTickIntervalMs);
                });
                services.AddSingleton<ITradingServiceFactory>(sp =>
                {
                    var opts = sp.GetRequiredService<IOptions<TradingOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    return opts.Provider == TradingProvider.Ctp
                        ? new CtpTradingServiceFactory(loggerFactory)
                        : new MockTradingServiceFactory(loggerFactory);
                });

                // ── 会话服务（登录编排：行情连接→交易连接→结算确认）──
                services.AddSingleton<ISessionService, SessionService>();

                // ── 窗口分组 + 窗口宿主 + 成组同步 ──
                services.AddSingleton<WindowGroupService>();
                services.AddSingleton<IWindowHost, WindowManager>();
                services.AddSingleton<GroupSynchronizationCoordinator>();

                // ── 合约窗口 VM 依赖（TradingViewModel 通过 ActivatorUtilities 创建，需 DI 解析剩余参数）──
                services.Configure<SoundOptions>(ctx.Configuration.GetSection("Sound"));
                services.AddSingleton<ISoundService, SoundService>();
                services.AddSingleton<IKeyboardOperationService, KeyboardOperationService>();
                services.AddSingleton<ITradingSessionChecker, TradingSessionChecker>();
                services.AddSingleton<ISpreadCalculator, SpreadCalculator>();
                services.AddTransient<IOrderValidator, OrderValidator>();

                // ── 本地风控（从 config.ini 加载）──
                services.AddSingleton<ILocalRiskService>(sp =>
                {
                    var configRepo = sp.GetRequiredService<IConfigRepository>();
                    var configOpts = sp.GetRequiredService<IOptions<ConfigFileOptions>>().Value;
                    var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                    try
                    {
                        var cloud = configRepo.Load(configOpts.Path);
                        Log.Information("本地风控已加载：RiskOpen={Risk} MaxInput={Input} MaxPos={Pos}",
                            cloud.Order.RiskOpen, cloud.Order.MaxInputCount, cloud.Order.MaxPositionCount);
                        return new LocalRiskService(cloud.Order, loggerFactory.CreateLogger<LocalRiskService>());
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "加载 config.ini Order 段失败，本地风控使用默认配置（关闭）");
                        return new LocalRiskService(new Domain.Configuration.OrderConfig(),
                            loggerFactory.CreateLogger<LocalRiskService>());
                    }
                });

                // ── 登录页 + 浮动栏 VM/Window（单例，整个会话复用）──
                services.AddSingleton<LoginViewModel>();
                services.AddSingleton<LoginWindow>();
                services.AddSingleton<FloatingMainViewModel>();
                services.AddSingleton<FloatingMainWindow>();

                // ── 设置窗口（瞬态：每次打开都是新实例，关闭即释放）──
                services.AddSingleton<WindowGroupBarViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsWindow>();

                // ── MCP（可选）──
                if (mcpEnabled)
                {
                    services.AddMcpServer()
                        .WithHttpTransport(o => o.Stateless = true)
                        .WithFuturesTraderConfigTools();
                }
            });

        if (mcpEnabled)
        {
            builder.ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(mcpUrl);
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/ping", () => "pong");
                        endpoints.MapMcp("/mcp");
                    });
                });
            });
            Log.Information("MCP 服务已启用，监听 {Url}（POST /mcp）", mcpUrl);
        }

        _host = builder.Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // 1. 单例守卫：已运行则激活首个实例并退出
        _singleInstance = new SingleInstanceGuard();
        _singleInstance.ActivateRequested += (_, _) => OnActivateRequested();
        if (!_singleInstance.TryAcquire())
        {
            Log.Information("检测到已运行实例，激活后退出");
            SingleInstanceGuard.SignalActivateExisting();
            Shutdown();
            return;
        }

        await _host.StartAsync();

        // 2. 确保 CTP 流文件目录存在（MdFlow/TraderFlow），CTP API 需要可写目录存储会话流
        EnsureFlowDirectories();

        // 3. 应用持久化主题
        var themeService = _host.Services.GetRequiredService<IThemeService>();
        if (themeService is ThemeService ts)
            ts.Apply(ts.LoadPersisted());
        else
            themeService.Apply(ApplicationTheme.Dark);

        // 3. 显示登录页 + 订阅登录成功事件
        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        var loginVm = loginWindow.ViewModel;
        loginVm.LoginSucceeded += OnLoginSucceeded;
        loginVm.OpenSettingsRequested += OnOpenSettingsRequested;
        loginWindow.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// 将相对路径基于 exe 目录（<see cref="AppContext.BaseDirectory"/>）解析为绝对路径。
    /// 绝对路径原样返回；null/空返回空串。确保无论从哪个工作目录启动，文件路径都能正确解析。
    /// </summary>
    private static string ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return System.IO.Path.IsPathRooted(path)
            ? path
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, path));
    }

    /// <summary>
    /// 确保 CTP 行情/交易流文件目录存在。CTP API 的 FlowPath 需要可写目录存储会话流文件，
    /// 目录缺失会导致 RegisterFront/Subscribe 行为异常。PostConfigure 已将路径绝对化。
    /// </summary>
    private void EnsureFlowDirectories()
    {
        try
        {
            var mdOpts = _host.Services.GetRequiredService<IOptions<MarketDataOptions>>().Value;
            var trOpts = _host.Services.GetRequiredService<IOptions<TradingOptions>>().Value;
            EnsureDirectory(mdOpts.FlowPath, "MdFlow");
            EnsureDirectory(trOpts.FlowPath, "TraderFlow");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "创建 CTP 流文件目录失败（将继续启动，CTP 连接时可能报错）");
        }
    }

    /// <summary>确保目录存在，不存在则创建（含父目录）。</summary>
    private static void EnsureDirectory(string? path, string label)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!System.IO.Directory.Exists(path))
        {
            System.IO.Directory.CreateDirectory(path);
            Log.Information("已创建 CTP 流目录 [{Label}]: {Path}", label, path);
        }
    }

    /// <summary>登录成功：显示浮动工具栏 + 关闭登录页。</summary>
    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var floating = _host.Services.GetRequiredService<FloatingMainWindow>();
            floating.ViewModel.LogoutRequested += OnLogoutRequested;
            floating.ViewModel.OpenSettingsRequested += OnOpenSettingsRequested;
            floating.Show();

            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            loginWindow.Hide();
        });
    }

    /// <summary>登出：关闭浮动栏 + 重新显示登录页。</summary>
    private void OnLogoutRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var floating = _host.Services.GetRequiredService<FloatingMainWindow>();
            floating.Hide();

            var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
            // 重置登录状态以便重新登录
            loginWindow.ViewModel.State = new LoginState.Idle();
            loginWindow.ViewModel.StatusMessage = "已登出，请重新登录";
            loginWindow.Show();
        });
    }

    /// <summary>打开设置窗口：从 DI 解析瞬态 SettingsWindow，设置 Owner 后 Show。
    /// 瞬态保证每次打开都是新实例，关闭即释放；WindowGroupBarViewModel 为单例（共享分组状态）。</summary>
    private void OnOpenSettingsRequested(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var settings = _host.Services.GetRequiredService<SettingsWindow>();
            // Owner 设为当前活跃窗口（登录页或浮动栏），跟随父窗口主题/位置
            var active = Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            if (active is not null)
            {
                settings.Owner = active;
            }
            settings.Show();
            Log.Information("设置窗口已打开");
        });
    }

    /// <summary>单例激活：把当前主窗口置顶。</summary>
    private void OnActivateRequested()
    {
        Dispatcher.Invoke(() =>
        {
            foreach (Window w in Current.Windows)
            {
                if (w is FloatingMainWindow || w is LoginWindow)
                {
                    w.Activate();
                    w.Topmost = true;
                    w.Topmost = false; // 闪烁后回落，保持 z-order
                    break;
                }
            }
        });
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            // 登出释放 CTP 连接
            var session = _host.Services.GetRequiredService<ISessionService>();
            await session.LogoutAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "退出时登出异常");
        }

        await _host.StopAsync();
        _host.Dispose();
        _singleInstance?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
