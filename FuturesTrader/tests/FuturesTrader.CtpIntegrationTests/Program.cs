using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;
using FuturesTrader.Infrastructure.Trading.Ctp;
using FuturesTrader.Infrastructure.Trading.Ctp.Native;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuturesTrader.CtpIntegrationTests;

/// <summary>
/// CTP 生产 API 的受控实机验证入口。
/// <para>
/// <c>--system-info</c> 严格离线：仅读取本机架构、复制到输出目录的 DLL PE 头和 API 版本。
/// <c>--live-readonly</c> 只允许认证、登录、结算确认、行情订阅与持仓/资金/合约查询；
/// 此文件没有任何报单或撤单调用路径。
/// </para>
/// <para>
/// 所有敏感输入都只从明确命名的环境变量取得，输出只报告阶段是否成功，绝不回显账号、密码、认证码、
/// 前置地址、资金、持仓、合约或订单明细。
/// </para>
/// </summary>
internal static class Program
{
    private const string BrokerIdVariable = "FUTURESTRADER_CTP_BROKER_ID";
    private const string UserIdVariable = "FUTURESTRADER_CTP_USER_ID";
    private const string PasswordVariable = "FUTURESTRADER_CTP_PASSWORD";
    private const string AppIdVariable = "FUTURESTRADER_CTP_APP_ID";
    private const string AuthCodeVariable = "FUTURESTRADER_CTP_AUTH_CODE";
    private const string MarketDataFrontVariable = "FUTURESTRADER_CTP_MD_FRONT";
    private const string TradingFrontVariable = "FUTURESTRADER_CTP_TRADING_FRONT";
    private const string SubscriptionVariable = "FUTURESTRADER_CTP_READONLY_INSTRUMENTS";
    private static readonly TimeSpan ReadOnlyTimeout = TimeSpan.FromSeconds(25);

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        return args switch
        {
            ["--system-info"] => RunSystemInfo(),
            ["--live-readonly"] => await RunLiveReadOnlyAsync(),
            _ => PrintUsage()
        };
    }

    /// <summary>
    /// 只读本机运行环境。此方法不创建服务对象、不注册前置地址，也不触发任何网络 I/O。
    /// </summary>
    private static int RunSystemInfo()
    {
        Console.WriteLine("CTP 生产 API 系统信息（离线）");
        Console.WriteLine($"操作系统: {RuntimeInformation.OSDescription}");
        Console.WriteLine($".NET: {RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"进程架构: {RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"配置 ApiRuntimeMode: {CtpApiRuntimeMode.Production}（bIsProductionMode=true）");

        try
        {
            var traderPath = ResolveNativeDll("thosttraderapi_se.dll");
            var marketDataPath = ResolveNativeDll("thostmduserapi_se.dll");
            var traderArchitecture = ReadPeMachine(traderPath);
            var marketDataArchitecture = ReadPeMachine(marketDataPath);
            var traderVersion = ThostTraderApiNative.GetApiVersion();
            var marketDataVersion = ThostMdApiNative.GetApiVersion();

            Console.WriteLine($"Trader DLL 架构: {traderArchitecture}");
            Console.WriteLine($"Md DLL 架构: {marketDataArchitecture}");
            Console.WriteLine($"Trader API 版本: {traderVersion}");
            Console.WriteLine($"Md API 版本: {marketDataVersion}");

            var valid = !string.Equals(traderVersion, "unknown", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(marketDataVersion, "unknown", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine(valid ? "系统信息结果: 通过" : "系统信息结果: 失败（未读取到 API 版本）");
            return valid ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"系统信息结果: 失败（{ex.GetType().Name}）");
            return 1;
        }
    }

    /// <summary>
    /// 用外部注入的测试账号执行严格只读链路。不得在这里增加订单、撤单或任何修改交易状态的 API 调用。
    /// </summary>
    private static async Task<int> RunLiveReadOnlyAsync()
    {
        var stage = "配置校验";
        if (!TryReadCredentials(out var credentials, out var missingVariables))
        {
            Console.Error.WriteLine("只读实测未启动：缺少环境变量。");
            Console.Error.WriteLine($"需要: {string.Join(", ", missingVariables)}");
            return 2;
        }

        // 先做无网络的本机 API 自检，避免在 DLL/架构不匹配时连接任何前置。
        if (RunSystemInfo() != 0)
            return 1;

        Console.WriteLine("CTP 只读实测：开始（生产 API 模式，敏感配置已隐藏）");
        var flowRoot = Path.Combine(Path.GetTempPath(), "FuturesTrader", "ctp-readonly", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(flowRoot);
        using var timeout = new CancellationTokenSource(ReadOnlyTimeout);

        try
        {
            var marketDataOptions = new MarketDataOptions
            {
                Provider = MarketDataProvider.Ctp,
                ApiRuntimeMode = CtpApiRuntimeMode.Production,
                FrontAddress = credentials.MarketDataFront,
                BrokerId = credentials.BrokerId,
                UserId = credentials.UserId,
                Password = credentials.Password,
                AppId = credentials.AppId,
                AuthCode = credentials.AuthCode,
                FlowPath = Path.Combine(flowRoot, "md"),
                PriceLadderLevels = 5
            };
            var tradingOptions = new TradingOptions
            {
                Provider = TradingProvider.Ctp,
                ApiRuntimeMode = CtpApiRuntimeMode.Production,
                FrontAddress = credentials.TradingFront,
                BrokerId = credentials.BrokerId,
                UserId = credentials.UserId,
                Password = credentials.Password,
                AppId = credentials.AppId,
                AuthCode = credentials.AuthCode,
                FlowPath = Path.Combine(flowRoot, "trader")
            };

            await using var marketData = new CtpMarketDataService(
                marketDataOptions,
                NullLogger<CtpMarketDataService>.Instance);
            await using var trading = new CtpTradingService(
                tradingOptions,
                NullLogger<CtpTradingService>.Instance);

            var marketDataConnected = 0;
            var marketDataReceived = 0;
            var tradingConnected = 0;
            var positionReceived = 0;
            var accountReceived = 0;
            var instrumentReceived = 0;

            using var marketDataConnectionSubscription = marketData.ConnectionStream.Subscribe(state =>
            {
                if (state is ConnectionState.Connected)
                    Interlocked.Exchange(ref marketDataConnected, 1);
            });
            using var marketDataSubscription = marketData.MarketDataStream.Subscribe(_ =>
                Interlocked.Exchange(ref marketDataReceived, 1));
            using var tradingConnectionSubscription = trading.ConnectionStream.Subscribe(state =>
            {
                if (state is ConnectionState.Connected)
                    Interlocked.Exchange(ref tradingConnected, 1);
            });
            using var positionSubscription = trading.PositionStream.Subscribe(_ =>
                Interlocked.Exchange(ref positionReceived, 1));
            using var accountSubscription = trading.AccountStream.Subscribe(_ =>
                Interlocked.Exchange(ref accountReceived, 1));
            using var instrumentSubscription = trading.InstrumentStream.Subscribe(_ =>
                Interlocked.Exchange(ref instrumentReceived, 1));

            stage = "行情连接与登录";
            await marketData.ConnectAsync(timeout.Token);
            Console.WriteLine(Volatile.Read(ref marketDataConnected) == 1
                ? "行情认证与登录: 通过"
                : "行情认证与登录: 已返回但未观察到 Connected 回报");

            var instruments = ReadSubscriptionInstruments();
            if (instruments.Count > 0)
            {
                stage = "行情订阅";
                await marketData.SubscribeAsync(instruments, timeout.Token);
                await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                Console.WriteLine(Volatile.Read(ref marketDataReceived) == 1
                    ? "行情订阅回报: 已收到"
                    : "行情订阅回报: 未在等待期收到（非交易时段或合约不可用时可出现）");
            }
            else
            {
                Console.WriteLine($"行情订阅: 跳过（可选环境变量 {SubscriptionVariable} 未配置）");
            }

            stage = "交易认证、登录与结算确认";
            await trading.ConnectAsync(timeout.Token);
            Console.WriteLine(Volatile.Read(ref tradingConnected) == 1
                ? "交易认证、登录与结算确认: 通过"
                : "交易认证、登录与结算确认: 已返回但未观察到 Connected 回报");

            // 以下全部是 CTP 查询 API。刻意不订阅 OrderStream，也不接触任何报单/撤单入口。
            stage = "持仓查询";
            await trading.QueryPositionAsync(cancellationToken: timeout.Token);
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            stage = "资金查询";
            await trading.QueryTradingAccountAsync(timeout.Token);
            await Task.Delay(TimeSpan.FromSeconds(1), timeout.Token);
            stage = "合约查询";
            await trading.QueryInstrumentAsync(cancellationToken: timeout.Token);
            await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);

            Console.WriteLine(Volatile.Read(ref positionReceived) == 1
                ? "持仓查询回报: 已收到（内容已隐藏）"
                : "持仓查询回报: 未收到（空仓或前置延迟时可出现）");
            Console.WriteLine(Volatile.Read(ref accountReceived) == 1
                ? "资金查询回报: 已收到（内容已隐藏）"
                : "资金查询回报: 未收到");
            Console.WriteLine(Volatile.Read(ref instrumentReceived) == 1
                ? "合约查询回报: 已收到（内容已隐藏）"
                : "合约查询回报: 未收到");

            var passed = trading.CurrentState is ConnectionState.Connected
                && marketData.CurrentState is ConnectionState.Connected
                && Volatile.Read(ref accountReceived) == 1;
            Console.WriteLine(passed ? "CTP 只读实测结果: 通过" : "CTP 只读实测结果: 未完全通过");
            return passed ? 0 : 1;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine($"CTP 只读实测结果: 超时（阶段：{stage}）");
            return 1;
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine($"CTP 只读实测结果: 超时（阶段：{stage}）");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CTP 只读实测结果: 失败（{ex.GetType().Name}）");
            return 1;
        }
    }

    private static bool TryReadCredentials(out ReadOnlyCredentials credentials, out IReadOnlyList<string> missingVariables)
    {
        var values = new Dictionary<string, string?>
        {
            [BrokerIdVariable] = Environment.GetEnvironmentVariable(BrokerIdVariable),
            [UserIdVariable] = Environment.GetEnvironmentVariable(UserIdVariable),
            [PasswordVariable] = Environment.GetEnvironmentVariable(PasswordVariable),
            [AppIdVariable] = Environment.GetEnvironmentVariable(AppIdVariable),
            [AuthCodeVariable] = Environment.GetEnvironmentVariable(AuthCodeVariable),
            [MarketDataFrontVariable] = Environment.GetEnvironmentVariable(MarketDataFrontVariable),
            [TradingFrontVariable] = Environment.GetEnvironmentVariable(TradingFrontVariable)
        };
        var missing = values
            .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key)
            .ToArray();
        missingVariables = missing;
        if (missing.Length > 0)
        {
            credentials = default!;
            return false;
        }

        credentials = new ReadOnlyCredentials(
            values[BrokerIdVariable]!,
            values[UserIdVariable]!,
            values[PasswordVariable]!,
            values[AppIdVariable]!,
            values[AuthCodeVariable]!,
            values[MarketDataFrontVariable]!,
            values[TradingFrontVariable]!);
        return true;
    }

    private static IReadOnlyList<string> ReadSubscriptionInstruments() =>
        (Environment.GetEnvironmentVariable(SubscriptionVariable) ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ResolveNativeDll(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"未找到输出目录中的 {fileName}");
        return path;
    }

    private static string ReadPeMachine(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        return peReader.PEHeaders.CoffHeader.Machine.ToString();
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("用法:");
        Console.Error.WriteLine("  dotnet run --project FuturesTrader/tests/FuturesTrader.CtpIntegrationTests -- --system-info");
        Console.Error.WriteLine("  dotnet run --project FuturesTrader/tests/FuturesTrader.CtpIntegrationTests -- --live-readonly");
        Console.Error.WriteLine("--live-readonly 仅使用 FUTURESTRADER_CTP_* 环境变量，且不会发送委托或撤单。");
        return 64;
    }

    private sealed record ReadOnlyCredentials(
        string BrokerId,
        string UserId,
        string Password,
        string AppId,
        string AuthCode,
        string MarketDataFront,
        string TradingFront);
}
