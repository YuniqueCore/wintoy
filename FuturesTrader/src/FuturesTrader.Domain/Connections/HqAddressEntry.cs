using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FuturesTrader.Domain.Connections;

/// <summary>
/// 行情上游地址条目：镜像 HQAddress.xml 的 &lt;Address&gt; 元素。
/// <para>示例 XML：<c>&lt;Address Name="海通" Port="38215"&gt;180.168.212.75&lt;/Address&gt;</c></para>
/// <para>
/// 实现原因：本类型直接绑定到登录页 DataGrid。<see cref="LatencyMs"/> 与 <see cref="ProbeSuccess"/>
/// 是测速后填充的**可变 UI 状态**，通过 <see cref="INotifyPropertyChanged"/> 通知 UI 刷新。
/// 若用 record + with 替换整个实例，会让 DataGrid.SelectedItem 引用失配而清空选中
/// （测速回调可能在用户输入密码期间到达，表现为"输完密码 DataGrid 选中就没了"）。
/// 因此身份字段（Name/Host/Port）保持 init 只读确保实例身份稳定，延迟字段可变 + INPC。
/// </para>
/// </summary>
public sealed class HqAddressEntry : INotifyPropertyChanged
{
    /// <summary>行情服务商简称（如"海通"、"东证联通"）。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>行情服务器 IP 地址（XML 元素文本）。</summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>行情服务器端口（XML Port 属性）。</summary>
    public int Port { get; init; }

    /// <summary>完整 TCP 连接地址：<c>tcp://{Host}:{Port}</c>。</summary>
    public string Url => $"tcp://{Host}:{Port}";

    private double? _latencyMs;

    /// <summary>延迟（毫秒），由测速服务填充；null 表示尚未测速。</summary>
    public double? LatencyMs
    {
        get => _latencyMs;
        set
        {
            if (_latencyMs != value)
            {
                _latencyMs = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _probeSuccess = true;

    /// <summary>测速是否成功（不可达时为 false）。</summary>
    public bool ProbeSuccess
    {
        get => _probeSuccess;
        set
        {
            if (_probeSuccess != value)
            {
                _probeSuccess = value;
                OnPropertyChanged();
            }
        }
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
