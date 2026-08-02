using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// WindowConfig 段的可编辑视图状态。
/// Domain record 是 init-only 不可变，VM 持有可变字段供双向绑定；
/// <see cref="Hydrate"/> 从 record 拷贝到 VM，<see cref="ToConfig"/> 用 VM 字段构造新 record。
/// PriceListRatios 集合本轮不暴露编辑，保存时保留原值。
/// </summary>
public sealed partial class WindowConfigViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string MainFont { get; set; } = "新宋体";

    [ObservableProperty]
    public partial int CompactSpacing { get; set; } = 7;

    [ObservableProperty]
    public partial int FontSizeOffset { get; set; }

    [ObservableProperty]
    public partial int PriceListMargin { get; set; } = 5;

    [ObservableProperty]
    public partial int DecTitle { get; set; } = 30;

    [ObservableProperty]
    public partial int Align { get; set; } = 1;

    [ObservableProperty]
    public partial int NarrowReduceLength { get; set; } = 40;

    [ObservableProperty]
    public partial int MouseWheelSpeed { get; set; } = 3;

    [ObservableProperty]
    public partial bool AutoSize { get; set; }

    [ObservableProperty]
    public partial int TickRowHeights { get; set; } = 12;

    [ObservableProperty]
    public partial int AskQuoteRowCount { get; set; } = 30;

    [ObservableProperty]
    public partial int BidQuoteRowCount { get; set; } = 30;

    [ObservableProperty]
    public partial int InstrumentWindowHeights { get; set; } = 1000;

    /// <summary>从 Domain record 拷贝到 VM 可变字段。</summary>
    public void Hydrate(WindowConfig w)
    {
        MainFont = w.MainFont;
        CompactSpacing = w.CompactSpacing;
        FontSizeOffset = w.FontSizeOffset;
        PriceListMargin = w.PriceListMargin;
        DecTitle = w.DecTitle;
        Align = w.Align;
        NarrowReduceLength = w.NarrowReduceLength;
        MouseWheelSpeed = w.MouseWheelSpeed;
        AutoSize = w.AutoSize;
        TickRowHeights = Math.Clamp(w.TickRowHeights, 10, 32);
        AskQuoteRowCount = Math.Clamp(w.AskQuoteRowCount, 5, 100);
        BidQuoteRowCount = Math.Clamp(w.BidQuoteRowCount, 5, 100);
        InstrumentWindowHeights = w.InstrumentWindowHeights;
    }

    /// <summary>用当前 VM 字段构造 Domain record（保留 PriceListRatios 原值）。</summary>
    public WindowConfig ToConfig(WindowConfig original) => original with
    {
        MainFont = MainFont,
        CompactSpacing = CompactSpacing,
        FontSizeOffset = FontSizeOffset,
        PriceListMargin = PriceListMargin,
        DecTitle = DecTitle,
        Align = Align,
        NarrowReduceLength = NarrowReduceLength,
        MouseWheelSpeed = MouseWheelSpeed,
        AutoSize = AutoSize,
        TickRowHeights = Math.Clamp(TickRowHeights, 10, 32),
        AskQuoteRowCount = Math.Clamp(AskQuoteRowCount, 5, 100),
        BidQuoteRowCount = Math.Clamp(BidQuoteRowCount, 5, 100),
        InstrumentWindowHeights = InstrumentWindowHeights
    };
}
