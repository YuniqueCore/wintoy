using FuturesTrader.Domain.MarketData;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Infrastructure.Mock;

/// <summary>
/// Mock 行情与交易共享的确定性领域目录。显式维护真实的代码、交易所、价格精度和合约乘数，
/// 动态服务只在这些约束内生成行情与账户快照。
/// </summary>
internal static class MockMarketCatalog
{
    internal static IReadOnlyList<MockInstrumentProfile> Profiles { get; } =
    [
        Future("ag2608", "SHFE", "白银2608", 1m, 15, 7332m, 7288m, 7296m, 428_600, 318_420, 180),
        Future("ag2609", "SHFE", "白银2609", 1m, 15, 7408m, 7362m, 7376m, 364_200, 296_840, 170),
        Future("ag2610", "SHFE", "白银2610", 1m, 15, 7522m, 7478m, 7491m, 306_900, 264_810, 160),
        Future("ag2612", "SHFE", "白银2612", 1m, 15, 7608m, 7562m, 7579m, 284_600, 238_420, 150),
        Future("ag2701", "SHFE", "白银2701", 1m, 15, 7685m, 7638m, 7652m, 226_800, 196_420, 130),
        Future("ag2702", "SHFE", "白银2702", 1m, 15, 7740m, 7696m, 7710m, 184_600, 168_240, 115),
        Future("ag2703", "SHFE", "白银2703", 1m, 15, 7802m, 7758m, 7772m, 156_800, 142_680, 100),
        Future("ag2704", "SHFE", "白银2704", 1m, 15, 7860m, 7818m, 7830m, 128_400, 118_260, 90),
        Future("ag2705", "SHFE", "白银2705", 1m, 15, 7926m, 7882m, 7898m, 106_200, 98_640, 80),
        Future("ag2706", "SHFE", "白银2706", 1m, 15, 7988m, 7940m, 7958m, 88_640, 82_460, 72),
        Future("ag2707", "SHFE", "白银2707", 1m, 15, 8046m, 8002m, 8018m, 72_480, 68_240, 65),
        Future("au2608", "SHFE", "黄金2608", 0.02m, 1000, 552.66m, 549.88m, 550.42m, 228_640, 168_420, 105),
        Future("au2610", "SHFE", "黄金2610", 0.02m, 1000, 555.84m, 552.36m, 553.10m, 186_420, 142_680, 90),
        Future("au2612", "SHFE", "黄金2612", 0.02m, 1000, 560.82m, 557.44m, 558.18m, 94_260, 106_540, 70),
        Future("cu2609", "SHFE", "沪铜2609", 10m, 5, 73_260m, 72_980m, 73_080m, 152_680, 221_460, 120),
        Future("rb2610", "SHFE", "螺纹钢2610", 1m, 10, 3_318m, 3_286m, 3_295m, 1_268_400, 1_842_600, 420),
        Future("sn2609", "SHFE", "沪锡2609", 10m, 1, 268_430m, 266_980m, 267_520m, 82_640, 74_260, 45),

        Future("jd2609", "DCE", "鸡蛋2609", 1m, 10, 3_350m, 3_326m, 3_338m, 268_400, 196_820, 150),
        Future("i2609", "DCE", "铁矿石2609", 0.5m, 100, 795.5m, 788m, 790.5m, 824_600, 668_420, 360),
        Future("m2609", "DCE", "豆粕2609", 1m, 10, 3_186m, 3_158m, 3_166m, 936_200, 1_264_800, 380),
        Future("p2609", "DCE", "棕榈油2609", 2m, 10, 8_426m, 8_332m, 8_368m, 428_600, 396_240, 220),
        Future("c2609", "DCE", "玉米2609", 1m, 10, 2_318m, 2_306m, 2_310m, 356_800, 728_400, 260),

        Future("SR609", "CZCE", "白糖609", 1m, 10, 5_842m, 5_806m, 5_818m, 286_420, 412_680, 140),
        Future("CF609", "CZCE", "棉花609", 5m, 5, 14_520m, 14_410m, 14_455m, 246_800, 526_400, 120),
        Future("TA609", "CZCE", "PTA609", 2m, 5, 5_188m, 5_142m, 5_160m, 618_400, 926_200, 280),
        Future("MA609", "CZCE", "甲醇609", 1m, 10, 2_468m, 2_438m, 2_450m, 526_800, 814_600, 240),
        Future("RM609", "CZCE", "菜粕609", 1m, 10, 2_742m, 2_718m, 2_728m, 362_400, 478_200, 180),

        Future("IF2609", "CFFEX", "沪深300股指2609", 0.2m, 300, 4_086.4m, 4_052.6m, 4_068.2m, 82_460, 126_840, 55, maxOrderVolume: 100),
        Future("IH2609", "CFFEX", "上证50股指2609", 0.2m, 300, 2_986.8m, 2_964.2m, 2_974.6m, 42_680, 68_240, 35, maxOrderVolume: 100),
        Future("IC2609", "CFFEX", "中证500股指2609", 0.2m, 200, 6_428.6m, 6_376.4m, 6_398.8m, 68_420, 112_680, 45, maxOrderVolume: 100),
        Future("IM2609", "CFFEX", "中证1000股指2609", 0.2m, 200, 7_236.2m, 7_168.8m, 7_198.4m, 96_280, 154_620, 60, maxOrderVolume: 100),

        Future("sc2609", "INE", "原油2609", 0.1m, 1000, 588.6m, 579.8m, 582.4m, 126_480, 84_260, 75),
        Future("nr2609", "INE", "20号胶2609", 5m, 10, 12_865m, 12_720m, 12_780m, 62_840, 48_620, 40),
        Future("si2609", "GFEX", "工业硅2609", 5m, 5, 10_485m, 10_360m, 10_415m, 186_240, 224_680, 95),
        Future("lc2609", "GFEX", "碳酸锂2609", 50m, 1, 104_850m, 102_600m, 103_450m, 284_620, 318_460, 130),

        Option("au2610C560", "SHFE", "黄金2610购560", 0.02m, 1000, 12.68m, 11.92m, 12.10m, 28_640, 18_260, 30, 560m, '1', "20260924"),
        Option("au2610P550", "SHFE", "黄金2610沽550", 0.02m, 1000, 9.36m, 10.14m, 9.88m, 24_680, 16_420, 28, 550m, '2', "20260924"),
        Option("m2609-C-3200", "DCE", "豆粕2609购3200", 0.5m, 10, 86.5m, 79.5m, 82m, 36_420, 42_680, 40, 3200m, '1', "20260807"),
        Option("jd2609-P-3200", "DCE", "鸡蛋2609沽3200", 0.5m, 10, 18.5m, 21m, 19.5m, 28_640, 32_480, 35, 3200m, '2', "20260807"),
        Option("jd2609-P-3300", "DCE", "鸡蛋2609沽3300", 0.5m, 10, 42.5m, 47m, 45m, 34_680, 38_240, 40, 3300m, '2', "20260807"),
    ];

    private static readonly IReadOnlyDictionary<string, MockInstrumentProfile> ById = Profiles
        .ToDictionary(profile => profile.Instrument.InstrumentId, StringComparer.OrdinalIgnoreCase);

    internal static MockInstrumentProfile FindOrFallback(string instrumentId) =>
        ById.TryGetValue(instrumentId, out var profile) ? profile : CreateFallback(instrumentId);

    internal static IReadOnlyList<Position> Positions { get; } =
    [
        Position("ag2608", Direction.Buy, today: 3, yesterday: 5, frozen: 1, averagePrice: 7_288m, profit: 5_460m),
        Position("ag2608", Direction.Sell, today: 1, yesterday: 1, frozen: 0, averagePrice: 7_365m, profit: 990m),
        Position("au2610", Direction.Buy, today: 1, yesterday: 2, frozen: 0, averagePrice: 552.18m, profit: 10_980m),
        Position("cu2609", Direction.Buy, today: 2, yesterday: 1, frozen: 0, averagePrice: 72_680m, profit: 8_700m),
        Position("jd2609", Direction.Sell, today: 2, yesterday: 3, frozen: 1, averagePrice: 3_382m, profit: 1_600m),
        Position("IF2609", Direction.Buy, today: 1, yesterday: 0, frozen: 0, averagePrice: 4_052.8m, profit: 10_080m),
    ];

    internal static TradingAccount Account { get; } = new()
    {
        AccountId = "mock-account",
        Balance = 1_268_430m,
        Available = 843_250m,
        Equity = 1_268_430m,
        MarketValue = 472_860m,
        PositionProfit = 37_810m,
        CloseProfit = 8_420m,
        Margin = 391_200m,
        FrozenMargin = 18_600m,
        FrozenCash = 2_480m,
        FrozenCommission = 86m,
        Commission = 1_286m,
        WithdrawBalance = 0m,
    };

    private static MockInstrumentProfile Future(
        string id, string exchange, string name, decimal tick, int multiplier,
        decimal last, decimal preSettlement, decimal open, long volume, long openInterest, int depth,
        int maxOrderVolume = 1000) =>
        Profile(id, exchange, name, tick, multiplier, last, preSettlement, open, volume, openInterest, depth,
            maxOrderVolume, (byte)'1', 0m, 0, string.Empty);

    private static MockInstrumentProfile Option(
        string id, string exchange, string name, decimal tick, int multiplier,
        decimal last, decimal preSettlement, decimal open, long volume, long openInterest, int depth,
        decimal strike, char optionsType, string expireDate) =>
        Profile(id, exchange, name, tick, multiplier, last, preSettlement, open, volume, openInterest, depth,
            50, (byte)'2', strike, (byte)optionsType, expireDate);

    private static MockInstrumentProfile Profile(
        string id, string exchange, string name, decimal tick, int multiplier,
        decimal last, decimal preSettlement, decimal open, long volume, long openInterest, int depth,
        int maxOrderVolume, byte productClass, decimal strike, byte optionsType, string expireDate) => new(
            new Instrument
            {
                InstrumentId = id,
                ExchangeId = exchange,
                Name = name,
                PriceTick = tick,
                VolumeMultiple = multiplier,
                MinLimitOrderVolume = 1,
                MaxLimitOrderVolume = maxOrderVolume,
                IsTrading = true,
                ProductClass = productClass,
                StrikePrice = strike,
                OptionsType = optionsType,
                ExpireDate = expireDate,
                CreateDate = "20260105",
            },
            last,
            preSettlement,
            open,
            volume,
            openInterest,
            depth);

    private static MockInstrumentProfile CreateFallback(string id) =>
        Future(id, "MOCK", $"模拟合约 {id}", 1m, 10, 1_000m, 992m, 996m, 12_680, 8_420, 40);

    private static Position Position(
        string instrumentId,
        Direction direction,
        int today,
        int yesterday,
        int frozen,
        decimal averagePrice,
        decimal profit)
    {
        var profile = FindOrFallback(instrumentId);
        var total = today + yesterday;
        return new Position
        {
            InstrumentId = instrumentId,
            InvestorId = "mock-investor",
            Direction = direction,
            HedgeFlag = HedgeFlag.Speculation,
            TodayPosition = today,
            YdPosition = yesterday,
            TotalPosition = total,
            FrozenPosition = frozen,
            PositionCost = averagePrice * total * profile.Instrument.VolumeMultiple,
            PositionProfit = profit,
            VolumeMultiple = profile.Instrument.VolumeMultiple,
        };
    }
}

/// <summary>一份合约元数据及其行情生成基线。</summary>
internal sealed record MockInstrumentProfile(
    Instrument Instrument,
    decimal InitialPrice,
    decimal PreSettlementPrice,
    decimal OpenPrice,
    long InitialVolume,
    long InitialOpenInterest,
    int TypicalDepthVolume);
