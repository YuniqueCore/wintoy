using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace FuturesTrader.Presentation.Converters;

/// <summary>
/// 延迟值 → 背景色转换器：绿 &lt;30ms / 黄 &lt;60ms / 红 ≥60ms / 灰 null 或不可达。
/// 用于登录页行情/交易地址列表的延迟色块。
/// </summary>
public sealed class LatencyToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4C, 0xAF, 0x50));
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(0xE6, 0xA8, 0x17));
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xE5, 0x39, 0x35));
    private static readonly SolidColorBrush Gray = new(Color.FromRgb(0x9E, 0x9E, 0x9E));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double latency) return Gray;
        if (latency < 30) return Green;
        if (latency < 60) return Yellow;
        return Red;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
