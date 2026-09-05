using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using SenSÉ.Shell.Localization;

using SenSÉ.Tools.Clipboard;

namespace SenSÉ.Desktop.Tools.Clipboard;

/// <summary>Hides the image row for text entries.</summary>
/// <remarks>
/// A converter rather than two templates: the rows are otherwise identical, and duplicating the
/// template to vary one element is how they drift apart.
/// </remarks>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public static NullToCollapsedConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// The clipboard history.
/// </summary>
/// <remarks>
/// Choosing an entry puts it back on the clipboard and pastes it into the window that had the
/// foreground before the environment took it. That is the part Win+V could not do here: a paste
/// goes to the focused window, and the overlay had made itself the focused window.
/// </remarks>
public partial class ClipboardWindow : Window
{
    public ClipboardWindow()
    {
        InitializeComponent();
        Entries.ItemsSource = ClipboardService.History.Entries;

        Loaded += (_, _) =>
        {
            if (Entries.Items.Count > 0)
            {
                Entries.SelectedIndex = 0;
            }

            Entries.Focus();
        };
    }

    private void Entry_Activated(object sender, MouseButtonEventArgs e) => PasteSelected();

    private void Entries_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Return)
        {
            PasteSelected();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void PasteSelected()
    {
        if (Entries.SelectedItem is not ClipboardEntry entry)
        {
            return;
        }

        // Closed first: the paste is sent to whichever window is in front, so this one must be gone
        // before the keystroke leaves. Closing also releases the foreground back to the target.
        Close();

        Status.Text = ClipboardService.PasteInto(entry)
            ? ""
            : Strings.Current["Copié. Aucune fenêtre où coller : utilisez Ctrl+V."];
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClipboardService.History.Clear();
        Status.Text = Strings.Current["Historique vidé."];
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
