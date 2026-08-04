using System.Globalization;
using System.Windows.Data;

namespace SteamXBox.Gui.Localization;

/// <summary>
/// Translates a value for display only.
/// </summary>
/// <remarks>
/// Used on combo box items whose values are written into the profile file: translating the list
/// itself would store "None" where the loader expects "Aucun", and would stop the current selection
/// from matching. Converting on the way out leaves the stored identifier untouched.
/// </remarks>
public sealed class LocalizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text ? Strings.Current[text] : value ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Display only; the stored value must never come from the label.");
}
