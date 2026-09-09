using System.IO;
using System.Windows;
using System.Text;
using System.Windows.Documents;

namespace SenSÉ.EditeurTexte.Format;

/// <summary>
/// Serialise/deserialise un <see cref="FlowDocument"/> en XAML WPF via <see cref="DataFormats.Xaml"/>.
/// Utilise par l'historique pour cloner les snapshots.
/// </summary>
public static class XamlRoundTrip
{
    public static string VersXaml(FlowDocument doc)
    {
        using var ms = new MemoryStream();
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        range.Save(ms, DataFormats.Xaml);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static void DepuisXaml(FlowDocument doc, string xaml)
    {
        if (string.IsNullOrEmpty(xaml)) return;
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
        range.Load(ms, DataFormats.Xaml);
    }
}
