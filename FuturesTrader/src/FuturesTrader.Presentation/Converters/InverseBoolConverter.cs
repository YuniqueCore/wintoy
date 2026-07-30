using System.Globalization;
using System.Windows.Data;

namespace FuturesTrader.Presentation.Converters;

/// <summary>
/// 布尔取反转换器：将 <see cref="bool"/> 取反后返回。
/// 仅支持 OneWay（bool→bool），用于 RadioButton 互斥表达（如 IsChgOrderA=true → A 选中，B 取反选中）。
/// <para>
/// 用法：<c>IsChecked="{Binding IsChgOrderA, Converter={StaticResource InverseBool}, Mode=OneWay}"</c>
/// </para>
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b ? !b : !true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // OneWay 仅：不支持回写
        return Binding.DoNothing;
    }
}
