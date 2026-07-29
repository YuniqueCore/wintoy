using System.Windows;
using FuturesTrader.Application;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Infrastructure.Persistence;
using FuturesTrader.Infrastructure.Persistence.WindowGroups;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.Views;
using FuturesTrader.Presentation.WindowHosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

                // 窗口分组管理：仓库(Users.xml+window-groups.json) + 窗口宿主(桩) + 编排服务 + 段 VM
                services.Configure<WindowLayoutOptions>(ctx.Configuration.GetSection("WindowLayout"));
                services.AddSingleton<IWindowGroupRepository, UsersXmlWindowGroupRepository>();
                services.AddSingleton<IWindowHost, StubWindowHost>();
                services.AddSingleton<WindowGroupService>();
                services.AddSingleton<WindowGroupBarViewModel>();

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
