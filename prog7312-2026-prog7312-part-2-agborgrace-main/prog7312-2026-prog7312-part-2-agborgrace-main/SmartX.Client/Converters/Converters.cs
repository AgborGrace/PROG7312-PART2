using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using SmartX.Client.Models;

namespace SmartX.Client.Converters;

/// <summary>Frozen brushes shared by converters and code-behind (frozen = cheaper and thread-safe).</summary>
public static class Palette
{
    public static readonly Brush Teal = Make(0x14, 0xB8, 0xA6);
    public static readonly Brush Amber = Make(0xF5, 0xA6, 0x23);
    public static readonly Brush Red = Make(0xEF, 0x44, 0x44);
    public static readonly Brush Grey = Make(0x7C, 0x87, 0x97);
    public static readonly Brush Blue = Make(0x4F, 0x8E, 0xF7);
    public static readonly Brush Light = Make(0xE7, 0xEC, 0xF3);
    public static readonly Brush SoftBlue = Make(0x9F, 0xB0, 0xC8);

    private static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Brush ForStatus(DeviceStatus status) => status switch
    {
        DeviceStatus.Normal => Teal,
        DeviceStatus.Warning => Amber,
        DeviceStatus.Critical => Red,
        DeviceStatus.Disconnected => Grey,
        _ => Blue   // AwaitingData
    };
}

/// <summary>DeviceStatus -> colour (teal healthy, amber warning, red critical, grey disconnected, blue awaiting data).</summary>
public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DeviceStatus status ? Palette.ForStatus(status) : Palette.Grey;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"Critical" / "Warning" / anything else -> red / amber / soft blue.</summary>
public class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Critical" => Palette.Red,
            "Warning" => Palette.Amber,
            _ => Palette.SoftBlue
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
