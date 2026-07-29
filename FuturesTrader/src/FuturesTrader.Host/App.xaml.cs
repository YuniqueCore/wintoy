using System.Windows;
using FuturesTrader.Application.Abstractions;
using FuturesTrader.Application.Options;
using FuturesTrader.Infrastructure.Persistence;
using FuturesTrader.Presentation.ViewModels;
using FuturesTrader.Presentation.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace FuturesTrader.Host;

/// <summary>
/// 应用入口：DI 装配 + Serilog 配置 + OnStartup 手动启动 MainWindow。
/// Host 是组合根：唯一同时引用 Application/Infrastructure/Presentation 的层。
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

        // DI 组合根：CreateDefaultBuilder 默认加载 appsettings.json + 环境变量
        _host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseSerilog(Log.Logger, dispose: true)
            .ConfigureServices((ctx, services) =>
            {
                services.Configure<ConfigFileOptions>(ctx.Configuration.GetSection("ConfigFile"));
                services.AddSingleton<IConfigRepository, ConfigRepository>();
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
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
