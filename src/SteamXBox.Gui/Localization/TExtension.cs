using System.Windows.Data;
using System.Windows.Markup;

namespace SteamXBox.Gui.Localization;

/// <summary>
/// Markup extension that binds a label to its translation: <c>Text="{loc:T Sensibilité pad}"</c>.
/// </summary>
/// <remarks>
/// It returns a binding rather than a plain string so the text updates the moment the language
/// changes, without reloading the views.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string text) => Text = text;

    /// <summary>The French source text, which doubles as the lookup key.</summary>
    [ConstructorArgument("text")]
    public string Text { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Text}]")
        {
            Source = Strings.Current,
            Mode = BindingMode.OneWay,
            FallbackValue = Text,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
