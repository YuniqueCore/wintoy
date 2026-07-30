using System.Globalization;
using System.Windows.Data;

namespace FuturesTrader.Presentation.Converters;

/// <summary>
/// 枚举 ↔ 布尔转换器：用于 RadioButton 绑定枚举字段（单选互斥）。
/// ConverterParameter 传枚举值名，匹配则 true。支持 OneWay（enum→bool）和 TwoWay（bool→enum，选中时写回）。
/// <para>
/// 用法：<c>IsChecked="{Binding OrderMode, Converter={StaticResource EnumToBool}, ConverterParameter=Open, Mode=TwoWay}"</c>
/// </para>
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is string paramStr && value is not null)
        {
            return value.ToString() == paramStr;
        }
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter is string paramStr && targetType.IsEnum)
        {
            return Enum.Parse(targetType, paramStr);
        }
        return Binding.DoNothing;
    }
}
