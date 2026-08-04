using System.Windows.Controls;

namespace SteamXBox.Gui.Views;

/// <summary>
/// The connected controllers, one numbered button each.
/// </summary>
/// <remarks>
/// No code of its own. The list is refreshed by whoever owns the view model — the tab that becomes
/// visible — rather than on a timer here: polling HID and XInput continuously to keep a decorative
/// list current would hold the same handles the bridge needs.
/// </remarks>
public partial class ControllerStripView : UserControl
{
    public ControllerStripView() => InitializeComponent();
}
