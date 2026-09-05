using System.Windows;
using System.Windows.Input;

namespace SenSÉ.Gui.Views;

/// <summary>
/// Asks for the name to give a controller, pre-filled with the one it currently answers to.
/// </summary>
/// <remarks>
/// A window rather than an inline editor on the chip: a chip is a button, and turning a button into
/// a text field mid-list is more machinery than a dialog. Enter confirms, Escape cancels, and the
/// name comes back through <see cref="ControllerName"/>.
/// </remarks>
public partial class RenameDialog : Window
{
    public RenameDialog(string currentName)
    {
        InitializeComponent();
        NameBox.Text = currentName;
        NameBox.SelectAll();
        Loaded += (_, _) => NameBox.Focus();
    }

    /// <summary>The name the user typed, or null if they cancelled.</summary>
    public string? ControllerName { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ControllerName = NameBox.Text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Ok_Click(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
    }
}
