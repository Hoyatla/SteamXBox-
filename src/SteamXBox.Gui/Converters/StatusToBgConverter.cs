using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SteamXBox.Gui.Views;

public sealed class StatusToBgConverter : IValueConverter
{
    public static readonly StatusToBgConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b
                ? new SolidColorBrush(Color.FromArgb(0x30, 0x44, 0xCC, 0x88))
                : new SolidColorBrush(Color.FromArgb(0x30, 0xCC, 0x44, 0x44));
        return new SolidColorBrush(Colors.Transparent);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
