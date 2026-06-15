using System;
using System.Globalization;
using System.Windows.Data;

namespace DesktopBox.Controls;

/// <summary>判断绑定值是否等于 ConverterParameter(枚举/对象),返回 bool。用于菜单项 IsChecked 勾选。</summary>
public class EnumEqualityToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && parameter is not null
           && value.Equals(parameter is string s ? Enum.Parse(value.GetType(), s) : parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
