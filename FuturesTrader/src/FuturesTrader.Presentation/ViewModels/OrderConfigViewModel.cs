using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// OrderConfig 段的可编辑视图状态（交易风控参数）。
/// 镜像 <see cref="WindowConfigViewModel"/> 的 Hydrate/ToConfig 双向映射模式：
/// Domain record 是 init-only 不可变，VM 持有可变字段供双向绑定。
/// </summary>
public sealed partial class OrderConfigViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool Spck { get; set; }

    [ObservableProperty]
    public partial bool Gzck { get; set; }

    [ObservableProperty]
    public partial bool RiskOpen { get; set; }

    [ObservableProperty]
    public partial int MaxCancelGz { get; set; } = 395;

    [ObservableProperty]
    public partial int MaxCancelSp { get; set; } = 10000;

    [ObservableProperty]
    public partial int MaxCancelQq { get; set; } = 10000;

    [ObservableProperty]
    public partial int MaxInputCount { get; set; }

    [ObservableProperty]
    public partial int MaxPositionCount { get; set; }

    /// <summary>从 Domain record 拷贝到 VM 可变字段。</summary>
    public void Hydrate(OrderConfig o)
    {
        Spck = o.Spck;
        Gzck = o.Gzck;
        RiskOpen = o.RiskOpen;
        MaxCancelGz = o.MaxCancelGz;
        MaxCancelSp = o.MaxCancelSp;
        MaxCancelQq = o.MaxCancelQq;
        MaxInputCount = o.MaxInputCount;
        MaxPositionCount = o.MaxPositionCount;
    }

    /// <summary>用当前 VM 字段构造 Domain record。</summary>
    public OrderConfig ToConfig() => new()
    {
        Spck = Spck,
        Gzck = Gzck,
        RiskOpen = RiskOpen,
        MaxCancelGz = MaxCancelGz,
        MaxCancelSp = MaxCancelSp,
        MaxCancelQq = MaxCancelQq,
        MaxInputCount = MaxInputCount,
        MaxPositionCount = MaxPositionCount
    };
}
