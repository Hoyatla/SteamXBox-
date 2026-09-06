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
        // On ecoute le mouvement au niveau parent
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