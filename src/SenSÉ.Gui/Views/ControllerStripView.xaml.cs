using System.Windows;
using System.Windows.Controls;
using SenSÉ.Gui.ViewModels;

namespace SenSÉ.Gui.Views;

/// <summary>
/// The connected controllers, one numbered button each.
/// </summary>
/// <remarks>
/// The list is refreshed by whoever owns the view model — the tab that becomes visible — rather
/// than on a timer here: polling HID and XInput continuously to keep a decorative list current
/// would hold the same handles the bridge needs. The only code here is the rename menu: a
/// ContextMenu lives outside the visual tree, so its DataContext was pointed at the right-clicked
/// chip from XAML and is read back from the sender.
/// </remarks>
public partial class ControllerStripView : UserControl
{
    public ControllerStripView() => InitializeComponent();

    private static ControllerSlotItem? ChipFrom(object sender)
        => (sender as FrameworkElement)?.DataContext as ControllerSlotItem;

    private void RenameMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ChipFrom(sender) is not { } chip || DataContext is not ControllerStripViewModel vm)
        {
            return;
        }

        vm.Rename(chip);
    }

    private void ResetNameMenuItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (ChipFrom(sender) is not { } chip || DataContext is not ControllerStripViewModel vm)
        {
            return;
        }

        vm.ResetName(chip);
    }
}
