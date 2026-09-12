using System.Windows.Data;

namespace v2rayN.Converters;

/// <summary>
/// Translates MaterialDesign Swatch.Name (English) to Chinese for display.
/// </summary>
public class SwatchNameConverter : IValueConverter
{
    private static readonly Dictionary<string, string> _zhMap = new()
    {
        ["red"] = "红色",
        ["pink"] = "粉色",
        ["purple"] = "紫色",
        ["deeppurple"] = "深紫色",
        ["indigo"] = "靛青色",
        ["blue"] = "蓝色",
        ["lightblue"] = "浅蓝色",
        ["cyan"] = "青色",
        ["teal"] = "蓝绿色",
        ["green"] = "绿色",
        ["lightgreen"] = "浅绿色",
        ["lime"] = "酸橙色",
        ["yellow"] = "黄色",
        ["amber"] = "琥珀色",
        ["orange"] = "橙色",
        ["deeporange"] = "深橙色",
        ["brown"] = "棕色",
        ["grey"] = "灰色",
        ["bluegrey"] = "蓝灰色",
    };

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        var name = value?.ToString()?.ToLower() ?? string.Empty;
        return _zhMap.TryGetValue(name, out var zh) ? zh : name;
    }

    public object? ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return null;
    }
}
