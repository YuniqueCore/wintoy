using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// UserConfig 段的可编辑视图状态（行情/交易连接与开盘抢单参数）。
/// 镜像 <see cref="WindowConfigViewModel"/> 的 Hydrate/ToConfig 双向映射模式。
/// MOrderTimes（9 个开盘抢单时间点）M2 仅展示不编辑，保存时保留原值。
/// </summary>
public sealed partial class UserConfigViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string HqAddress { get; set; } = "tcp://140.207.230.97:61213";

    [ObservableProperty]
    public partial int Qdp { get; set; }

    [ObservableProperty]
    public partial int RunMode { get; set; }

    [ObservableProperty]
    public partial bool CloudRiskOn { get; set; }

    [ObservableProperty]
    public partial bool HqffOn { get; set; }

    [ObservableProperty]
    public partial string HqffIp { get; set; } = "127.0.0.1";

    [ObservableProperty]
    public partial int HqffPort { get; set; } = 56789;

    [ObservableProperty]
    public partial int MOrderXSpeed { get; set; } = 200;

    [ObservableProperty]
    public partial int MOrderXStop { get; set; } = 2200;

    /// <summary>9 个开盘抢单触发时间点（只读展示，M2 不编辑，保存时保留原值）。</summary>
    public ObservableCollection<string> MOrderTimes { get; } = [];

    /// <summary>从 Domain record 拷贝到 VM 可变字段。</summary>
    public void Hydrate(UserConfig u)
    {
        HqAddress = u.HqAddress;
        Qdp = u.Qdp;
        RunMode = u.RunMode;
        CloudRiskOn = u.CloudRiskOn;
        HqffOn = u.HqffOn;
        HqffIp = u.HqffIp;
        HqffPort = u.HqffPort;
        MOrderXSpeed = u.MOrderXSpeed;
        MOrderXStop = u.MOrderXStop;

        MOrderTimes.Clear();
        foreach (var t in u.MOrderTimes)
            MOrderTimes.Add(t.ToString("HH:mm:ss"));
    }

    /// <summary>用当前 VM 字段构造 Domain record，MOrderTimes 保留 original 原值。</summary>
    public UserConfig ToConfig(UserConfig original) => original with
    {
        HqAddress = HqAddress,
        Qdp = Qdp,
        RunMode = RunMode,
        CloudRiskOn = CloudRiskOn,
        HqffOn = HqffOn,
        HqffIp = HqffIp,
        HqffPort = HqffPort,
        MOrderXSpeed = MOrderXSpeed,
        MOrderXStop = MOrderXStop
    };
}
