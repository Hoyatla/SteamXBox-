using System.ComponentModel;
using System.Globalization;

namespace SteamXBox.Gui.Localization;

/// <summary>
/// Runtime string translation, keyed by the French source text.
/// </summary>
/// <remarks>
/// Using the source text as the key keeps the XAML readable and means nothing has to be kept in sync
/// between a key table and the markup: a string with no entry simply shows as written. Raising
/// PropertyChanged for the indexer refreshes every bound label, so switching language takes effect
/// without restarting.
/// </remarks>
public sealed class Strings : INotifyPropertyChanged
{
    public static Strings Current { get; } = new();

    private AppLanguage _language = AppLanguage.System;
    private bool _translating;

    public event PropertyChangedEventHandler? PropertyChanged;

    private Strings()
    {
        Apply(AppLanguage.System);
    }

    /// <summary>Language actually in effect, with <see cref="AppLanguage.System"/> already resolved.</summary>
    public AppLanguage Effective { get; private set; } = AppLanguage.French;

    public AppLanguage Language
    {
        get => _language;
        set => Apply(value);
    }

    /// <summary>Looks up a French source string. Unknown text is returned unchanged.</summary>
    public string this[string french] =>
        _translating && Translations.English.TryGetValue(french, out var translated) ? translated : french;

    /// <summary>
    /// Translates a composite format string, then fills it in. The key has to carry {0}-style
    /// placeholders rather than interpolated values, or every distinct value would be its own key.
    /// </summary>
    public string Format(string french, params object?[] args)
    {
        try
        {
            return string.Format(this[french], args);
        }
        catch (FormatException)
        {
            // A translation with mismatched placeholders must not take the app down.
            return french;
        }
    }

    public void Apply(AppLanguage language)
    {
        _language = language;
        Effective = language == AppLanguage.System ? DetectSystemLanguage() : language;
        _translating = Effective == AppLanguage.English;

        // "Item[]" is the WPF convention for "every indexer binding is stale".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Effective)));
    }

    /// <summary>
    /// French only when Windows is actually running in French; everything else gets English, which is
    /// the safer default for a language we have not translated.
    /// </summary>
    private static AppLanguage DetectSystemLanguage()
    {
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            return culture.TwoLetterISOLanguageName.Equals("fr", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.French
                : AppLanguage.English;
        }
        catch
        {
            return AppLanguage.English;
        }
    }
}
