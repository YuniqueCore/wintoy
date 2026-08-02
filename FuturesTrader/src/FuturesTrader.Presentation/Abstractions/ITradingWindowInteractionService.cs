using FuturesTrader.Domain.Configuration;
using FuturesTrader.Domain.Trading;

namespace FuturesTrader.Presentation.Abstractions;

/// <summary>
/// 浮动栏对当前存活合约窗口发出的全局交易交互意图。
/// 所有权属于窗口宿主：浮动栏不读取或维护其窗口字典，也不会创建尚未打开的窗口。
/// </summary>
public interface ITradingWindowInteractionService
{
    /// <summary>将仓/平意图应用到全部已创建的合约窗口（含当前隐藏的其他组）。</summary>
    void ApplyOnlyOpenToOpenWindows(bool onlyOpen);

    /// <summary>将 A/B 单选意图应用到全部已创建的合约窗口（含当前隐藏的其他组）。</summary>
    void ApplyOrderPlacementModeToOpenWindows(OrderPlacementMode placementMode);

    /// <summary>显示或隐藏价格梯无人报价行，并作为随后新建合约窗口的会话默认值。</summary>
    void ApplyWhiteGridVisibilityToOpenWindows(bool showWhiteGrid);

    /// <summary>将 Settings 中的共享价格梯显示配置应用到全部已创建合约窗口。</summary>
    void ApplyWindowDisplayConfigurationToOpenWindows(WindowConfig configuration);

    /// <summary>把当前可见合约窗口的价格梯定位到最优买价或最优卖价。</summary>
    void RecenterVisiblePriceLadders(PriceLadderAnchor anchor);
}

public enum PriceLadderAnchor
{
    Ask,
    Bid
}
