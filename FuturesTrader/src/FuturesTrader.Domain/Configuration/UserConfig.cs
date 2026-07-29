namespace FuturesTrader.Domain.Configuration;

/// <summary>
/// 对应 config.ini [User] 段：行情/交易连接与开盘抢单参数。
/// </summary>
public sealed record UserConfig
{
    /// <summary>默认行情服务器地址</summary>
    public string HqAddress { get; init; } = "tcp://140.207.230.97:61213";

    /// <summary>QDP 标记（语义未确认）</summary>
    public int Qdp { get; init; }

    /// <summary>运行模式（0=正常）</summary>
    public int RunMode { get; init; }

    /// <summary>云风控开关</summary>
    public bool CloudRiskOn { get; init; }

    /// <summary>行情转发开关</summary>
    public bool HqffOn { get; init; }

    /// <summary>行情转发目标 IP</summary>
    public string HqffIp { get; init; } = "127.0.0.1";

    /// <summary>行情转发目标端口</summary>
    public int HqffPort { get; init; } = 56789;

    /// <summary>开盘抢单频率（毫秒）</summary>
    public int MOrderXSpeed { get; init; } = 200;

    /// <summary>开盘抢单持续时间（毫秒）</summary>
    public int MOrderXStop { get; init; } = 2200;

    /// <summary>密码（明文，旧版遗留，重构时废弃）</summary>
    public string Pw { get; init; } = string.Empty;

    /// <summary>9 个开盘抢单触发时间点，覆盖各交易所开盘时段</summary>
    public IReadOnlyList<TimeOnly> MOrderTimes { get; init; } =
    [
        new(9, 29, 58),
        new(8, 59, 58),
        new(8, 54, 58),
        new(12, 59, 58),
        new(20, 59, 58),
        new(13, 29, 58),
        new(20, 54, 58),
        new(9, 24, 58),
        new(10, 31, 0)
    ];
}
