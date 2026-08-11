using System.Windows;
using System.Windows.Controls;
using SteamXBox.Plugins;

namespace SteamXBox.Desktop.Tools;

/// <summary>
/// The window a declarative tool gets, drawn by the host from what the tool describes.
/// </summary>
/// <remarks>
/// The most constraining rule of the contract, and the most important one. Ten tools written by ten
/// people, each drawing its own window, would be ten foreign windows — none of them following the
/// theme, none of them navigable with a controller. Here the tool says what it contains and this
/// draws it, so a tool written by a user is reachable with a gamepad without its author having
/// thought about controllers at all.
///
/// <para>
/// The cost is stated in the contract and accepted: a tool cannot have an interface the host has no
/// words for. The vocabulary grows when a real tool asks, never in anticipation.
/// </para>
/// </remarks>
public partial class PluginPanelWindow : Window
{
    private readonly PluginManifest? _manifest;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, Func<string>> _values = [];

    private PluginPanelWindow(PluginManifest manifest, Action<string>? log)
    {
        InitializeComponent();

        _manifest = manifest;
        _log = log;

        Title = manifest.Name;
        PanelTitle.Text = manifest.Name;
        PanelHint.Text = manifest.Hint;

        Build(PluginMemory.Load(manifest, log), manifest);
    }

    /// <summary>
    /// Opens the tool's panel, or brings back the one already open.
    /// </summary>
    /// <remarks>
    /// A window that fails to construct is still registered with the application, half-built, with
    /// its fields unset. Looking through that list without allowing for it turned one broken panel
    /// into a tile that threw a different exception on every press afterwards — the first fault
    /// hidden behind the second. The construction is guarded and the search tolerates a window that
    /// never finished.
    /// </remarks>
    public static string Open(PluginManifest manifest, Action<string>? log)
    {
        var existing = Application.Current.Windows.OfType<PluginPanelWindow>()
            .FirstOrDefault(window => window._manifest?.Id == manifest.Id);

        if (existing is not null)
        {
            existing.Activate();
            return "";
        }

        try
        {
            new PluginPanelWindow(manifest, log).Show();

            return "";
        }
        catch (Exception exception)
        {
            log?.Invoke($"plugin panel '{manifest.Id}' could not be drawn: "
                + $"{exception.GetType().Name}: {exception.Message}");

            return $"{manifest.Name} : le panneau n'a pas pu être dessiné.";
        }
    }

    /// <summary>Lays out one control per declared element.</summary>
    private void Build(IReadOnlyDictionary<string, string> remembered, PluginManifest manifest)
    {
        foreach (var item in manifest.Content)
        {
            var stored = item.Id.Length > 0 && remembered.TryGetValue(item.Id, out var saved) ? saved : item.Value;

            switch (item.Kind.ToLowerInvariant())
            {
                case "text":
                    Add(item, Text(item, stored));
                    break;

                case "number":
                    Add(item, Number(item, stored));
                    break;

                case "choice":
                    Add(item, Choice(item, stored));
                    break;

                case "file":
                    Add(item, FilePicker(item, stored));
                    break;

                case "action":
                    Body.Children.Add(ActionButton(item));
                    break;
            }
        }
    }

    /// <summary>A labelled row, which is the only layout a tool gets to ask for.</summary>
    private void Add(PluginContentItem item, FrameworkElement control)
    {
        if (item.Label.Length > 0)
        {
            Body.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 2),
                Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            });
        }

        Body.Children.Add(control);
    }

    private TextBox Text(PluginContentItem item, string value)
    {
        var box = new TextBox { Text = value, Padding = new Thickness(6, 4, 6, 4) };

        Remember(item, () => box.Text);

        return box;
    }

    /// <summary>
    /// A number, bounded by what the tool declared.
    /// </summary>
    /// <remarks>
    /// A slider rather than a text field: it cannot be given a value outside the range, so the host
    /// never has to validate what a tool's user typed, and it is usable with a controller — a text
    /// field would summon the on-screen keyboard for a number between one and sixty.
    /// </remarks>
    private Slider Number(PluginContentItem item, string value)
    {
        var slider = new Slider
        {
            Minimum = item.Min,
            Maximum = Math.Max(item.Min + 1, item.Max),
            Value = double.TryParse(value, out var parsed) ? parsed : item.Min,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            AutoToolTipPlacement = System.Windows.Controls.Primitives.AutoToolTipPlacement.TopLeft,
        };

        Remember(item, () => ((int)slider.Value).ToString());

        return slider;
    }

    /// <summary>
    /// A document the user designates, and nothing else.
    /// </summary>
    /// <remarks>
    /// The panel is the host's, so the picker is the host's, and the tool receives one path — the
    /// one that was pointed at. It never sees the folder it came from, never lists anything, and
    /// cannot reach a second file. That is the powerbox the contract describes, and it is what lets
    /// a tool touch a document without being trusted with the disk.
    ///
    /// <para>
    /// Not remembered between sessions even when the tool asks. A path to somebody's document is
    /// the one value here that is about them rather than about the tool.
    /// </para>
    /// </remarks>
    private Grid FilePicker(PluginContentItem item, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var path = new TextBox
        {
            Text = "",
            IsReadOnly = true,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var browse = new Button
        {
            Content = "Choisir…",
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(6, 0, 0, 0),
        };

        browse.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = item.Label.Length > 0 ? item.Label : "Choisir un document",
                Filter = item.Options.Count > 0
                    ? $"Documents ({string.Join(", ", item.Options.Select(o => "*." + o))})|"
                      + string.Join(";", item.Options.Select(o => "*." + o))
                    : "Tous les fichiers|*.*",
                CheckFileExists = true,
            };

            if (dialog.ShowDialog(this) == true)
            {
                path.Text = dialog.FileName;
                PanelStatus.Text = "";
            }
        };

        Grid.SetColumn(path, 0);
        Grid.SetColumn(browse, 1);
        grid.Children.Add(path);
        grid.Children.Add(browse);

        _values[item.Id] = () => path.Text;
        _notRemembered.Add(item.Id);

        return grid;
    }

    /// <summary>Values that are about the user rather than about the tool.</summary>
    private readonly HashSet<string> _notRemembered = [];

    private ComboBox Choice(PluginContentItem item, string value)
    {
        var box = new ComboBox { Padding = new Thickness(6, 4, 6, 4) };

        foreach (var option in item.Options)
        {
            box.Items.Add(option);
        }

        box.SelectedItem = item.Options.Contains(value) ? value : item.Options.FirstOrDefault();

        Remember(item, () => box.SelectedItem?.ToString() ?? "");

        return box;
    }

    /// <summary>
    /// A button that runs one of the host's actions.
    /// </summary>
    /// <remarks>
    /// The target may name a value from the panel, written <c>{id}</c>. That is as close to
    /// computation as a level-one tool gets: substituting a value the user chose, never working one
    /// out.
    /// </remarks>
    private Button ActionButton(PluginContentItem item)
    {
        var button = new Button
        {
            Content = item.Label.Length > 0 ? item.Label : item.Does,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // Run off the interface thread, always.
        //
        // Converting a document is not instant: a real one took seventy-one seconds, and on the
        // interface thread that is seventy-one seconds of frozen SteamXBox — full screen, refusing
        // to minimise, sitting under everything. From the user's side the machine had died.
        //
        // Every action goes the same way, not only the slow one. Launching an application can block
        // on a network path, and a rule that applies to the action known to be slow today is a rule
        // that will be missed on the next one.
        button.Click += async (_, _) =>
        {
            Save();

            var does = item.Does;
            var target = Substitute(item.Target);

            button.IsEnabled = false;
            PanelProgress.Visibility = Visibility.Visible;
            PanelStatus.Text = "En cours…";

            try
            {
                var log = _log;
                var said = await Task.Run(() => PluginTools.Perform(does, target, log));

                PanelStatus.Text = said.Length > 0 ? said : "Terminé.";
            }
            catch (Exception exception)
            {
                _log?.Invoke($"plugin action '{does}' failed: {exception.GetType().Name}: {exception.Message}");
                PanelStatus.Text = exception.Message;
            }
            finally
            {
                PanelProgress.Visibility = Visibility.Collapsed;
                button.IsEnabled = true;
            }
        };

        return button;
    }

    /// <summary>Replaces <c>{id}</c> in a target with what the panel currently holds.</summary>
    private string Substitute(string target)
    {
        if (!target.Contains('{'))
        {
            return target;
        }

        foreach (var (id, read) in _values)
        {
            target = target.Replace('{' + id + '}', read(), StringComparison.OrdinalIgnoreCase);
        }

        return target;
    }

    /// <summary>Registers a value, if the tool asked for it to be kept.</summary>
    /// <remarks>
    /// Only what <c>remembers</c> names. What is not named is not kept, so a tool cannot quietly
    /// persist something its manifest does not show.
    /// </remarks>
    private void Remember(PluginContentItem item, Func<string> read)
    {
        if (item.Id.Length > 0)
        {
            _values[item.Id] = read;
        }
    }

    private void Save()
        => PluginMemory.Save(
            _manifest!,
            _values.Where(pair => _manifest!.Remembers.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
                                  && !_notRemembered.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value()),
            _log);

    protected override void OnClosed(EventArgs e)
    {
        Save();
        base.OnClosed(e);
    }
}
