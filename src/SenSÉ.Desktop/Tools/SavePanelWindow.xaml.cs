using System.IO;
using System.Windows;
using System.Windows.Input;
using SenSÉ.Tools.Declarative;

namespace SenSÉ.Desktop.Tools;

/// <summary>
/// The host's save panel: the only way a tool reaches a file outside its own folder.
/// </summary>
/// <remarks>
/// The powerbox. A tool never sees the file system; it asks for this panel, the user designates a
/// file, and the host hands back a <see cref="FileGrant"/> covering that one. What the tool learns
/// is the grant, never the tree it was chosen from.
///
/// <para>
/// Started at the user's own folders rather than at a drive root. A tool asking to save should open
/// where the user's documents are, and the roots are the ones the launch vocabulary already knows —
/// one list, so a folder that can be opened is a folder that can be saved into.
/// </para>
/// </remarks>
public partial class SavePanelWindow : Window
{
    private string _directory = "";

    private SavePanelWindow(SaveRequest request, string purpose)
    {
        InitializeComponent();

        PurposeText.Text = string.IsNullOrWhiteSpace(purpose) ? "Enregistrer" : purpose;
        FileName.Text = FileGrant.SafeSuggestion(request);

        GoTo(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        // The name, ready to be replaced, with the extension left out of the selection: renaming is
        // the common case and retyping ".txt" every time is not.
        Loaded += (_, _) =>
        {
            FileName.Focus();
            var dot = FileName.Text.LastIndexOf('.');
            FileName.Select(0, dot > 0 ? dot : FileName.Text.Length);
        };
    }

    /// <summary>
    /// Asks the user where to save, and returns permission for that one file, or null.
    /// </summary>
    /// <param name="owner">The environment window, so the panel is modal to it rather than lost behind.</param>
    /// <param name="request">What the tool proposes to call the file.</param>
    /// <param name="purpose">One line saying who is asking and what for.</param>
    public static FileGrant? Ask(Window? owner, SaveRequest request, string purpose = "")
    {
        var panel = new SavePanelWindow(request, purpose);

        if (owner is not null && !ReferenceEquals(owner, panel))
        {
            panel.Owner = owner;
        }

        return panel.ShowDialog() == true ? panel.Grant : null;
    }

    /// <summary>The permission issued, once the user has designated a file.</summary>
    public FileGrant? Grant { get; private set; }

    /// <summary>Lists a directory: the way up, then folders, then files.</summary>
    /// <remarks>
    /// Files are shown although only folders can be entered. A save panel that hides them lets the
    /// user overwrite a neighbour without ever having seen it.
    /// </remarks>
    private void GoTo(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            _directory = Path.GetFullPath(directory);
            CurrentPath.Text = _directory;
            Entries.Items.Clear();

            if (Directory.GetParent(_directory) is { } parent)
            {
                Entries.Items.Add(new Entry(parent.FullName, "..", IsDirectory: true));
            }

            foreach (var path in Directory.EnumerateDirectories(_directory).OrderBy(p => p))
            {
                Entries.Items.Add(new Entry(path, Path.GetFileName(path), IsDirectory: true));
            }

            foreach (var path in Directory.EnumerateFiles(_directory).OrderBy(p => p))
            {
                Entries.Items.Add(new Entry(path, Path.GetFileName(path), IsDirectory: false));
            }

            Complain("");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A folder Windows will not open is a message, not a closed panel: the user still has
            // the one they came from.
            Complain($"Dossier inaccessible : {exception.Message}");
        }
    }

    private void Entries_Activate(object sender, MouseButtonEventArgs e) => Activate(Entries.SelectedItem);

    private void Entries_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            Activate(Entries.SelectedItem);
            e.Handled = true;
        }
    }

    /// <summary>A folder is entered; a file becomes the name to save under.</summary>
    private void Activate(object? item)
    {
        if (item is not Entry entry)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            GoTo(entry.Path);
            return;
        }

        FileName.Text = entry.Label;
        FileName.Focus();
    }

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        // Reduced to a file name again before it is joined. The box is a text field, so it can hold
        // a path by the time it is read — typed, or pasted — and joining that to the current folder
        // would let a name reach outside the folder the user is looking at.
        var name = FileGrant.SafeSuggestion(new SaveRequest(FileName.Text, ""));

        if (string.IsNullOrWhiteSpace(FileName.Text))
        {
            Complain("Donnez un nom au fichier.");
            return;
        }

        if (FileGrant.Issue(Path.Combine(_directory, name)) is not { } grant)
        {
            Complain("Ce nom ne convient pas.");
            return;
        }

        if (File.Exists(grant.Path)
            && MessageBox.Show(
                this,
                $"« {name} » existe déjà. Le remplacer ?",
                "Enregistrer",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Grant = grant;
        DialogResult = true;
    }

    private void Complain(string message)
    {
        Problem.Text = message;
        Problem.Visibility = message.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>One line of the list. <c>ToString</c> is what the list shows.</summary>
    private sealed record Entry(string Path, string Label, bool IsDirectory)
    {
        public override string ToString() => IsDirectory ? $"📁  {Label}" : $"      {Label}";
    }
}
