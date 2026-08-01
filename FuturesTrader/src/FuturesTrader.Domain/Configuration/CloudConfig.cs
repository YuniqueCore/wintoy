namespace FuturesTrader.Domain.Configuration;

/// <summary>
/// 对应 config.ini 全部三段配置，是旧软件运行参数的完整快照。
/// WPF 重构后可作为 <see cref="Infrastructure.Persistence.ConfigRepository"/> 的读写模型，
/// 也可序列化为 JSON 替代 INI 格式。
/// </summary>
public sealed record CloudConfig
{
    public WindowConfig Window { get; init; } = new();
    public OrderConfig Order { get; init; } = new();
    public UserConfig User { get; init; } = new();
    public ShortcutConfig Shortcuts { get; init; } = new();
}
