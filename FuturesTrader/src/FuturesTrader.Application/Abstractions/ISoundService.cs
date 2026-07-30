namespace FuturesTrader.Application.Abstractions;

/// <summary>
/// 提示音类型枚举：对齐旧软件 TSoundWin 配置的 4 个 wav 文件（见 05-ui-windows.md §5.9）。
/// 事件→音效映射：成交通知→Chimes、报单错误/资金不足→NoMoney、撤单→Cancel、其他回报→CashRegister。
/// </summary>
public enum SoundType
{
    /// <summary>Nomoney.wav — 资金不足 / 报单错误。</summary>
    NoMoney,

    /// <summary>cashreg.wav — 通用回报提示（成交/撤单成功以外的回报）。</summary>
    CashRegister,

    /// <summary>chimes.wav — 成交通知（OnRtnTrade）。</summary>
    Chimes,

    /// <summary>Cancellation.wav — 撤单回报。</summary>
    Cancel
}

/// <summary>
/// 提示音服务抽象：全局单例，集中化播放 4 种事件音效（TSoundWin 复刻）。
/// 实现 MUST 线程安全（CTP 回调在工作线程触发 Play），且不阻塞调用方。
/// 文件缺失等错误应吞掉并记日志，避免影响交易主流程。
/// </summary>
public interface ISoundService
{
    /// <summary>播放指定类型音效（找不到文件/播放失败时静默降级）。</summary>
    void Play(SoundType type);

    /// <summary>启用/禁用全部音效（用户在 TSoundWin 关闭提示音时置 false）。</summary>
    bool Enabled { get; set; }
}
