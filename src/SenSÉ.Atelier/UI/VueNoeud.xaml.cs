using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class VueNoeud : UserControl
{
    public Noeud Noeud { get; }
    public event Action<Noeud>? Selectionnee;
    public event Action<Noeud, double, double>? Deplacement;

    private bool _drag;
    private Point _debut;
    private double _origX, _origY;

    public bool EstFocused { get; private set; }
    public bool EstSelectionne { get; private set; }

    public void DefinirSelection(bool sel)
    {
        EstSelectionne = sel;
        MettreAJourBord();
    }

    public void DefinirFocused(bool foc)
    {
        EstFocused = foc;
        MettreAJourBord();
    }

    private void MettreAJourBord()
    {
        if (Bord is null) return;
        if (EstSelectionne && EstFocused)
            Bord.Style = (Style)FindResource("BordFocusedSelected");
        else if (EstSelectionne)
            Bord.Style = (Style)FindResource("BordSelected");
        else if (EstFocused)
            Bord.Style = (Style)FindResource("BordFocused");
        else
            Bord.Style = (Style)FindResource("BordNorm");
    }

    private void Bord_GotFocus(object sender, RoutedEventArgs e)
    {
        DefinirFocused(true);
    }

    private void Bord_LostFocus(object sender, RoutedEventArgs e)
    {
        DefinirFocused(false);
    }

    public event Action<Noeud, KeyEventArgs>? NoeudTouche;

    private void Bord_KeyDown(object sender, KeyEventArgs e)
    {
        // Touche Escape : deselectionne le focus
        if (e.Key == Key.Escape)
        {
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }
        // Propager au Canvas parent (pour navigation fleches)
        NoeudTouche?.Invoke(Noeud, e);
    }

    public VueNoeud(Noeud n)
    {
        Noeud = n;
        InitializeComponent();
        var def = CatalogueNoeuds.Trouver(n.Type);
        Titre.Text = def?.Nom ?? n.Type;
        SousTitre.Text = n.Type;
        ListeEntrees.ItemsSource = n.PortsEntree;
        ListeSorties.ItemsSource = n.PortsSortie;
        Canvas.SetLeft(this, n.X);
        Canvas.SetTop(this, n.Y);
        MouseLeftButtonDown += (s, e) => { Selectionnee?.Invoke(n); e.Handled = true; };
        // Attache le Noeud parent a chaque VuePort genere
        ListeEntrees.ItemContainerGenerator.StatusChanged += (s, e) => AttacherPorts(ListeEntrees);
        ListeSorties.ItemContainerGenerator.StatusChanged += (s, e) => AttacherPorts(ListeSorties);
        Loaded += (s, e) => { AttacherPorts(ListeEntrees); AttacherPorts(ListeSorties); MettreAJourBord(); };
    }

    private void AttacherPorts(ItemsControl liste)
    {
        if (liste.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
        for (int i = 0; i < liste.Items.Count; i++)
        {
            var c = liste.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (c is null) continue;
            var port = liste.Items[i] as Port;
            if (port is null) continue;
            // Trouver le VuePort dans l'arbre visuel
            var vp = Descendre<VuePort>(c);
            if (vp is null) continue;
            vp.AttacherParent(Noeud, port);
        }
    }

    private static T? Descendre<T>(DependencyObject d) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            var r = Descendre<T>(c);
            if (r is not null) return r;
        }
        return null;
    }

    private void Entete_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Selectionnee?.Invoke(Noeud);
        _drag = true;
        _debut = e.GetPosition(VisualParent as UIElement);
        _origX = Noeud.X;
        _origY = Noeud.Y;
        ((UIElement)VisualParent).CaptureMouse();
        e.Handled = true;
        if (VisualParent is UIElement parent)
        {
            parent.PreviewMouseMove -= Parent_MouseMove;
            parent.PreviewMouseMove += Parent_MouseMove;
            parent.PreviewMouseUp -= Parent_MouseUp;
            parent.PreviewMouseUp += Parent_MouseUp;
        }
    }

    private void Parent_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(VisualParent as UIElement);
        var dx = p.X - _debut.X;
        var dy = p.Y - _debut.Y;
        Noeud.X = _origX + dx;
        Noeud.Y = _origY + dy;
        Canvas.SetLeft(this, Noeud.X);
        Canvas.SetTop(this, Noeud.Y);
        Deplacement?.Invoke(Noeud, dx, dy);
    }

    private void Parent_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag)
        {
            _drag = false;
            ((UIElement)VisualParent).ReleaseMouseCapture();
        }
    }
}