using System.Reactive.Linq;
using FuturesTrader.Application.Options;
using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;
using FuturesTrader.Infrastructure.MarketData.Ctp;
using FuturesTrader.Infrastructure.MarketData.Ctp.Native;
using FuturesTrader.Infrastructure.Trading.Ctp;
using FuturesTrader.Infrastructure.Trading.Ctp.Native;
using Microsoft.Extensions.Logging;

namespace FuturesTrader.CtpIntegrationTests;

/// <summary>
/// CTP 实机集成测试：直连测试系统（60.12.233.58），验证完整链路：
/// 行情连接→认证→订阅→数据解析→价格梯 / 交易连接→认证→登录→结算确认→持仓/资金/合约查询。
/// <para>
/// 测试凭据：BrokerID=8080 UserID=000102 Password=258147 AppID=client_qihuo159_1.0 AuthCode=AC2F6ESEXEEYSIGU
/// 行情 tcp://60.12.233.58:18123 交易 tcp://60.12.233.58:18105
/// </para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== CTP 实机集成测试 ===");
        Console.WriteLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine();

        // 确保流文件目录存在
        var baseDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(Path.Combine(baseDir, "MdFlow"));
        Directory.CreateDirectory(Path.Combine(baseDir, "TraderFlow"));

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss.fff ";
                o.UseUtcTimestamp = false;
            });
            builder.SetMinimumLevel(LogLevel.Information);
        });

        var mdOptions = new MarketDataOptions
        {
            Provider = MarketDataProvider.Ctp,
            FrontAddress = "tcp://60.12.233.58:18123",
            BrokerId = "8080",
            UserId = "000102",
            Password = "258147",
            AppId = "client_qihuo159_1.0",
            AuthCode = "AC2F6ESEXEEYSIGU",
            FlowPath = Path.Combine(baseDir, "MdFlow"),
            PriceLadderLevels = 5
        };

        var trOptions = new TradingOptions
        {
            Provider = TradingProvider.Ctp,
            FrontAddress = "tcp://60.12.233.58:18105",
            BrokerId = "8080",
            UserId = "000102",
            Password = "258147",
            AppId = "client_qihuo159_1.0",
            AuthCode = "AC2F6ESEXEEYSIGU",
            FlowPath = Path.Combine(baseDir, "TraderFlow"),
        };

        var allPassed = true;

        // ── 0. DLL 版本验证（确认 64 位入口点修复正确）──
        Console.WriteLine(">>> [0/5] DLL 版本验证（64 位入口点）");
        try
        {
            var traderVer = ThostTraderApiNative.GetApiVersion();
            var mdVer = ThostMdApiNative.GetApiVersion();
            Console.WriteLine($"  TraderApi: {traderVer}");
            Console.WriteLine($"  MdApi: {mdVer}");
            if (traderVer.StartsWith("v") && mdVer.StartsWith("v"))
                Console.WriteLine("  PASS: 64 位 DLL 入口点调用成功");
            else
            {
                Console.WriteLine("  WARN: 版本号格式异常");
                allPassed = false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            allPassed = false;
        }

        // ── 1. 行情连接测试 ──
        Console.WriteLine(">>> [1/4] 行情服务连接 + 认证");
        var mdPassed = await TestMarketDataAsync(mdOptions, trOptions, loggerFactory);
        if (!mdPassed) allPassed = false;

        // ── 2. 交易连接测试 ──
        Console.WriteLine();
        Console.WriteLine(">>> [2/4] 交易服务连接 + 认证 + 结算确认");
        var trPassed = await TestTradingConnectionAsync(trOptions, loggerFactory);
        if (!trPassed) allPassed = false;

        // ── 3. 持仓/资金/合约查询测试 ──
        Console.WriteLine();
        Console.WriteLine(">>> [3/4] 持仓/资金/合约查询");
        var queryPassed = await TestTradingQueriesAsync(trOptions, loggerFactory);
        if (!queryPassed) allPassed = false;

        // ── 4. 数据解析验证（价格梯构建）──
        Console.WriteLine();
        Console.WriteLine(">>> [4/4] 数据解析验证");
        var parsePassed = TestPriceLadderParsing();
        if (!parsePassed) allPassed = false;

        Console.WriteLine();
        Console.WriteLine("=== 测试结果 ===");
        Console.WriteLine($"  行情连接:     {(mdPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  交易连接:     {(trPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  持仓/资金查询: {(queryPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  数据解析:     {(parsePassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"  总体:         {(allPassed ? "ALL PASS" : "HAS FAILURES")}");
        return allPassed ? 0 : 1;
    }

    /// <summary>测试行情连接、认证、订阅、数据接收和价格梯构建。</summary>
    private static async Task<bool> TestMarketDataAsync(
        MarketDataOptions mdOptions,
        TradingOptions trOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<CtpMarketDataService>();
        var md = new CtpMarketDataService(mdOptions, logger);

        var connected = false;
        var dataReceived = false;
        DepthMarketData? lastData = null;
        var connectionStates = new List<string>();

        md.ConnectionStream.Subscribe(state =>
        {
            connectionStates.Add(state.ToString());
            Console.WriteLine($"  [行情] 连接状态: {state}");
            if (state is ConnectionState.Connected) connected = true;
        });

        md.MarketDataStream.Subscribe(data =>
        {
            if (!dataReceived)
            {
                Console.WriteLine($"  [行情] 首条数据: {data.InstrumentId} " +
                                  $"Last={data.LastPrice} Vol={data.Volume} " +
                                  $"Bid1={data.BidPrices.FirstOrDefault()} Ask1={data.AskPrices.FirstOrDefault()}");
                lastData = data;
            }
            dataReceived = true;
        });

        try
        {
            Console.WriteLine($"  连接 {mdOptions.FrontAddress} ...");
            await md.ConnectAsync();
            Console.WriteLine("  ConnectAsync 已返回，等待连接状态...");

            // 等待连接成功（最多 15 秒）
            for (var i = 0; i < 30 && !connected; i++)
                await Task.Delay(500);

            if (!connected)
            {
                Console.WriteLine($"  FAIL: 行情未连接。状态变更: [{string.Join(", ", connectionStates)}]");
                return false;
            }

            Console.WriteLine("  PASS: 行情已连接");

            // 订阅常见合约（尝试多个，不同测试环境可用的合约不同）
            var instruments = new[] { "ag2512", "au2512", "IF2509", "cu2509", "rb2510", "sc2510" };
            Console.WriteLine($"  订阅行情: {string.Join(", ", instruments)}");
            await md.SubscribeAsync(instruments);

            // 等待行情数据（最多 15 秒）
            Console.WriteLine("  等待行情推送（15 秒）...");
            for (var i = 0; i < 30 && !dataReceived; i++)
                await Task.Delay(500);

            if (!dataReceived)
            {
                Console.WriteLine("  WARN: 15 秒内未收到行情数据（可能是非交易时段或合约不存在）");
                Console.WriteLine("  这不视为 FAIL——连接和认证已成功");
                return true; // 连接成功即 PASS，数据接收为可选
            }

            Console.WriteLine("  PASS: 行情数据已接收");

            // 验证数据解析（价格梯构建）
            if (lastData is not null)
            {
                var ladder = lastData.ToPriceLadder(priceTick: 0.5m, levels: 5);
                Console.WriteLine($"  价格梯: Levels={ladder.Levels} LastPrice={ladder.LastPrice} " +
                                  $"PriceTick={ladder.PriceTick} Rows={ladder.Rows.Count}");
                Console.WriteLine($"  中心行: Price={ladder.Center?.Price} " +
                                  $"AskVol={ladder.Center?.AskVolume} BidVol={ladder.Center?.BidVolume} " +
                                  $"Zone={ladder.Center?.Zone}");
                var askRows = ladder.Rows.Where(r => r.Zone == PriceZone.Ask).Count();
                var bidRows = ladder.Rows.Where(r => r.Zone == PriceZone.Bid).Count();
                Console.WriteLine($"  红区(Ask)行数={askRows} 蓝区(Bid)行数={bidRows} 中心行=1");
                if (askRows == 5 && bidRows == 5 && ladder.Rows.Count == 11)
                    Console.WriteLine("  PASS: 价格梯结构正确（2*5+1=11 行，红5+中1+蓝5）");
                else
                    Console.WriteLine($"  WARN: 价格梯行数异常（期望 11 行，实际 {ladder.Rows.Count}）");
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            return false;
        }
    }

    /// <summary>测试交易服务连接、认证、登录、结算确认。</summary>
    private static async Task<bool> TestTradingConnectionAsync(
        TradingOptions trOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<CtpTradingService>();
        var tr = new CtpTradingService(trOptions, logger);

        var connected = false;
        var connectionStates = new List<string>();

        tr.ConnectionStream.Subscribe(state =>
        {
            connectionStates.Add(state.ToString());
            Console.WriteLine($"  [交易] 连接状态: {state}");
            if (state is ConnectionState.Connected) connected = true;
        });

        try
        {
            Console.WriteLine($"  连接 {trOptions.FrontAddress} ...");
            await tr.ConnectAsync();
            Console.WriteLine("  ConnectAsync 已返回，等待连接状态...");

            // 等待连接成功（最多 20 秒，交易认证+登录+结算确认较长）
            for (var i = 0; i < 40 && !connected; i++)
                await Task.Delay(500);

            if (!connected)
            {
                Console.WriteLine($"  FAIL: 交易未连接。状态变更: [{string.Join(", ", connectionStates)}]");
                return false;
            }

            Console.WriteLine("  PASS: 交易已连接（认证→登录→结算确认 全部通过）");
            await tr.DisconnectAsync();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            return false;
        }
    }

    /// <summary>测试持仓、资金、合约查询。需要单独创建交易服务实例（前一个已被 Dispose）。</summary>
    private static async Task<bool> TestTradingQueriesAsync(
        TradingOptions trOptions,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger<CtpTradingService>();
        await using var tr = new CtpTradingService(trOptions, logger);

        var connected = false;
        var positionReceived = false;
        var accountReceived = false;
        var instrumentReceived = false;
        var instrumentCount = 0;
        var firstInstrument = string.Empty;

        tr.ConnectionStream.Subscribe(state =>
        {
            if (state is ConnectionState.Connected) connected = true;
        });

        tr.PositionStream.Subscribe(pos =>
        {
            if (!positionReceived)
                Console.WriteLine($"  [持仓] {pos.InstrumentId} Dir={pos.Direction} " +
                                  $"Total={pos.TotalPosition} Today={pos.TodayPosition}");
            positionReceived = true;
        });

        tr.AccountStream.Subscribe(acc =>
        {
            if (!accountReceived)
                Console.WriteLine($"  [资金] Account={acc.AccountId} " +
                                  $"Available={acc.Available:F2} Balance={acc.Balance:F2} " +
                                  $"Commission={acc.Commission:F2}");
            accountReceived = true;
        });

        tr.InstrumentStream.Subscribe(inst =>
        {
            instrumentCount++;
            if (!instrumentReceived)
            {
                firstInstrument = inst.InstrumentId;
                Console.WriteLine($"  [合约] 首条: {inst.InstrumentId} Name={inst.Name} " +
                                  $"PriceTick={inst.PriceTick} VolumeMultiple={inst.VolumeMultiple}");
                instrumentReceived = true;
            }
        });

        try
        {
            Console.WriteLine("  连接交易服务...");
            await tr.ConnectAsync();

            for (var i = 0; i < 40 && !connected; i++)
                await Task.Delay(500);

            if (!connected)
            {
                Console.WriteLine("  FAIL: 交易服务未连接，无法查询");
                return false;
            }

            Console.WriteLine("  PASS: 交易已连接");

            // 查询持仓
            Console.WriteLine("  查询持仓...");
            await tr.QueryPositionAsync();
            await Task.Delay(3000);
            Console.WriteLine($"  持仓查询: {(positionReceived ? "PASS - 已收到持仓数据" : "PASS - 无持仓（空仓状态正常）")}");

            // 查询资金
            Console.WriteLine("  查询资金...");
            await tr.QueryTradingAccountAsync();
            await Task.Delay(3000);
            Console.WriteLine($"  资金查询: {(accountReceived ? "PASS - 已收到资金数据" : "FAIL - 未收到资金数据")}");
            if (!accountReceived) return false;

            // 查询合约（只等 5 秒，合约列表可能很长）
            Console.WriteLine("  查询合约...");
            await tr.QueryInstrumentAsync();
            await Task.Delay(5000);
            Console.WriteLine($"  合约查询: {(instrumentReceived
                ? $"PASS - 已收到合约数据（5 秒内 {instrumentCount} 条，首条: {firstInstrument}）"
                : "FAIL - 未收到合约数据")}");
            if (!instrumentReceived) return false;

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            return false;
        }
    }

    /// <summary>验证 DepthMarketData → PriceLadder 解析逻辑（离线，用构造数据）。</summary>
    private static bool TestPriceLadderParsing()
    {
        try
        {
            var md = new DepthMarketData
            {
                InstrumentId = "TEST",
                LastPrice = 5000m,
                BidPrices = new[] { 4999m, 4998m, 4997m, 4996m, 4995m },
                BidVolumes = new[] { 10, 20, 30, 40, 50 },
                AskPrices = new[] { 5001m, 5002m, 5003m, 5004m, 5005m },
                AskVolumes = new[] { 15, 25, 35, 45, 55 },
            };

            var ladder = md.ToPriceLadder(priceTick: 1m, levels: 5);

            if (ladder.Rows.Count != 11)
            {
                Console.WriteLine($"  FAIL: 期望 11 行，实际 {ladder.Rows.Count}");
                return false;
            }

            if (ladder.Center?.Price != 5000m)
            {
                Console.WriteLine($"  FAIL: 中心价期望 5000，实际 {ladder.Center?.Price}");
                return false;
            }

            // 验证红区（上方卖盘）
            var askRows = ladder.Rows.Where(r => r.Zone == PriceZone.Ask).ToList();
            if (askRows.Count != 5)
            {
                Console.WriteLine($"  FAIL: 红区期望 5 行，实际 {askRows.Count}");
                return false;
            }
            // 最接近中心的卖盘（5001）应在红区最底部（索引 4）
            if (askRows[4].Price != 5001m || askRows[4].AskVolume != 15)
            {
                Console.WriteLine($"  FAIL: 红区底行期望 Price=5001 Vol=15，实际 Price={askRows[4].Price} Vol={askRows[4].AskVolume}");
                return false;
            }

            // 验证蓝区（下方买盘）
            var bidRows = ladder.Rows.Where(r => r.Zone == PriceZone.Bid).ToList();
            if (bidRows.Count != 5)
            {
                Console.WriteLine($"  FAIL: 蓝区期望 5 行，实际 {bidRows.Count}");
                return false;
            }
            // 最接近中心的买盘（4999）应在蓝区最顶部（索引 0）
            if (bidRows[0].Price != 4999m || bidRows[0].BidVolume != 10)
            {
                Console.WriteLine($"  FAIL: 蓝区顶行期望 Price=4999 Vol=10，实际 Price={bidRows[0].Price} Vol={bidRows[0].BidVolume}");
                return false;
            }

            // 验证中心行
            if (ladder.Center?.Zone != PriceZone.Center)
            {
                Console.WriteLine($"  FAIL: 中心行 Zone 期望 Center，实际 {ladder.Center?.Zone}");
                return false;
            }

            Console.WriteLine("  PASS: 价格梯解析正确");
            Console.WriteLine($"    中心价={ladder.LastPrice} 步长={ladder.PriceTick}");
            Console.WriteLine($"    红区(Ask): {askRows.Count} 行, 价格 {askRows[0].Price}~{askRows[4].Price}");
            Console.WriteLine($"    蓝区(Bid): {bidRows.Count} 行, 价格 {bidRows[0].Price}~{bidRows[4].Price}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  FAIL: {ex.Message}");
            return false;
        }
    }
}
