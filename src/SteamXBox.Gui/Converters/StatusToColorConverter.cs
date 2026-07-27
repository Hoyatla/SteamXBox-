using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace SteamXBox.Gui.Views;

public sealed class StatusToColorConverter : IValueConverter
{
    public static readonly StatusToColorConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b
                ? new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x88))
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));

        if (value is string s)
        {
            if (s.Contains("En cours") || s.Contains("Connecté"))
                return new SolidColorBrush(Color.FromRgb(0x44, 0xCC, 0x88));
            if (s.Contains("Arrêté"))
                return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
            if (s.Contains("Aucun"))
                return new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44));
        }

        return new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x99));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
