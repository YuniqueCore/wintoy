namespace FuturesTrader.Domain.Configuration;

/// <summary>
/// 合约交易窗口快捷键配置。使用 WPF <c>KeyGestureConverter</c> 可解析的文本持久化，
/// 领域层不依赖 Windows UI 类型。
/// </summary>
public sealed record ShortcutConfig
{
    public string SelectiveCancelAll { get; init; } = "Space";
    public string ForceCancelAll { get; init; } = "W";
    public string RecenterAsk { get; init; } = "A";
    public string RecenterBid { get; init; } = "D";
    public string ToggleOnlyOpen { get; init; } = "F";
    public string MoveSelectionUp { get; init; } = "Up";
    public string MoveSelectionDown { get; init; } = "Down";
}
