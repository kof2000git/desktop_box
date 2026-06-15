using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DesktopBox.Controls;

/// <summary>桌面图标可见 → 眼睛描边颜色:显示=亮蓝,隐藏=暗灰。</summary>
public class BoolToEyeStrokeConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new(Color.FromRgb(0x4F, 0xC3, 0xF7));   // 亮蓝
    private static readonly SolidColorBrush Off = new(Color.FromRgb(0x77, 0x77, 0x77));  // 暗灰

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? On : Off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>桌面图标可见 → 眼睛填充:显示=亮蓝(实心),隐藏=透明(空心)。</summary>
public class BoolToEyeFillConverter : IValueConverter
{
    private static readonly SolidColorBrush On = new(Color.FromRgb(0x4F, 0xC3, 0xF7));
    private static readonly SolidColorBrush Off = Brushes.Transparent;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? On : Off;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
