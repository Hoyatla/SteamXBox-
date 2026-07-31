using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SteamXBox.Gui.Converters;

public class ProfileHighlightConverter : IMultiValueConverter
{
    private static readonly Brush DefaultBg = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x33));
    private static readonly Brush ActiveBg = new SolidColorBrush(Color.FromRgb(0x44, 0x88, 0xFF));
    private static readonly Brush NormalBg = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x44));

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return NormalBg;
        var name = values[0]?.ToString();
        var activeName = values[1]?.ToString();

        if (name == "Default") return DefaultBg;
        if (!string.IsNullOrEmpty(activeName) && name == activeName) return ActiveBg;
        return NormalBg;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
