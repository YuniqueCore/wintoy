using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FuturesTrader.Domain.Configuration;

namespace FuturesTrader.Presentation.ViewModels;

/// <summary>
/// UserConfig 段的可编辑视图状态（行情/交易连接与开盘抢单参数）。
/// 镜像 <see cref="WindowConfigViewModel"/> 的 Hydrate/ToConfig 双向映射模式：
/// Domain record 是 init-only 不可变，VM 持有可变字段供双向绑定。
/// <para>
/// MOrderTimes（开盘抢单时间点，最长 <see cref="MaxMOrderTimes"/> 个）支持 Add/Remove CRUD：
/// 新增时校验 HH:mm:ss 格式、去重、不超上限；删除按索引。
/// </para>
/// </summary>
public sealed partial class UserConfigViewModel : ObservableObject
{
    /// <summary>MOrderTimes 上限：覆盖各交易所开盘时段的 9 个时间点。</summary>
    public const int MaxMOrderTimes = 9;

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

    /// <summary>开盘抢单触发时间点（字符串形式 HH:mm:ss，可 CRUD）。</summary>
    public ObservableCollection<string> MOrderTimes { get; } = [];

    /// <summary>新增 MOrderTime 的输入框（HH:mm:ss 格式）。</summary>
    [ObservableProperty]
    public partial string NewMOrderTime { get; set; } = "";

    /// <summary>最近一次 CRUD 失败的错误信息，UI 提示用，null 表示无错误。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddMOrderTimeCommand))]
    public partial string? MOrderTimeError { get; private set; }

    /// <summary>当前集合是否已满（9 个），用于禁用 Add 按钮。</summary>
    public bool IsMOrderTimesFull => MOrderTimes.Count >= MaxMOrderTimes;

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
        MOrderTimeError = null;
    }

    /// <summary>用当前 VM 字段构造 Domain record，包含 MOrderTimes 的 CRUD 结果。</summary>
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
        MOrderXStop = MOrderXStop,
        MOrderTimes = MOrderTimes
            .Select(ParseTimeOrThrow)
            .ToArray()
    };

    // ── MOrderTimes CRUD ─────────────────────────────────────────────

    /// <summary>新增一个开盘抢单时间点：校验 HH:mm:ss 格式 + 去重 + 不超上限。</summary>
    [RelayCommand(CanExecute = nameof(CanAddMOrderTime))]
    private void AddMOrderTime()
    {
        var trimmed = (NewMOrderTime ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            MOrderTimeError = "时间不能为空";
            return;
        }
        if (!TryParseFlexibleTime(trimmed, out var parsed))
        {
            MOrderTimeError = $"时间格式错误：{trimmed}（应为 HH:mm:ss，例如 09:29:58）";
            return;
        }
        var normalized = parsed.ToString("HH:mm:ss");
        if (MOrderTimes.Contains(normalized, StringComparer.Ordinal))
        {
            MOrderTimeError = $"重复时间：{normalized}";
            return;
        }
        if (MOrderTimes.Count >= MaxMOrderTimes)
        {
            MOrderTimeError = $"已达上限 {MaxMOrderTimes} 个";
            return;
        }

        MOrderTimes.Add(normalized);
        NewMOrderTime = "";
        MOrderTimeError = null;
        OnPropertyChanged(nameof(IsMOrderTimesFull));
    }

    private bool CanAddMOrderTime() =>
        !IsMOrderTimesFull
        && !string.IsNullOrWhiteSpace(NewMOrderTime);

    /// <summary>
    /// 解析时间字符串：先按严格 HH:mm:ss，再回退到 "h:m:s"（自动补零）。
    /// 例外：9:5:7 → 09:05:07。
    /// </summary>
    private static bool TryParseFlexibleTime(string input, out TimeOnly result)
    {
        if (TimeOnly.TryParseExact(input, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;
        var parts = input.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            && h is >= 0 and < 24
            && m is >= 0 and < 60
            && s is >= 0 and < 60)
        {
            try
            {
                result = new TimeOnly(h, m, s);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }
        result = default;
        return false;
    }

    /// <summary>删除指定索引的时间点（ItemsControl 传入索引）。</summary>
    [RelayCommand]
    private void RemoveMOrderTime(string? time)
    {
        if (time is null) return;
        if (!MOrderTimes.Remove(time)) return;
        MOrderTimeError = null;
        OnPropertyChanged(nameof(IsMOrderTimesFull));
    }

    /// <summary>输入框变化时清空错误提示并通知 AddCommand 的 CanExecute。</summary>
    partial void OnNewMOrderTimeChanged(string value)
    {
        MOrderTimeError = null;
        AddMOrderTimeCommand.NotifyCanExecuteChanged();
    }

    private static TimeOnly ParseTimeOrThrow(string s) =>
        TimeOnly.ParseExact(s, "HH:mm:ss", CultureInfo.InvariantCulture);
}
