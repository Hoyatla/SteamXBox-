using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class CanvasAtelier : UserControl
{
    public const string DragFormatPort = "Atelier.PortRef";

    private Graphe? _graphe;
    private readonly Dictionary<string, VueNoeud> _vuesNoeuds = new();
    private Noeud? _selection;

    public event Action<Noeud>? SelectionNoeud;
    public event Action<Noeud, double, double>? NoeudDeplace;
    public event Action? GrapheModifie;

    public CanvasAtelier()
    {
        InitializeComponent();
        Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
    }

    public Graphe? Graphe => _graphe;
    public Noeud? Selection => _selection;

    public void ChargerGraphe(Graphe? g)
    {
        _graphe = g;
        _vuesNoeuds.Clear();
        Surface.Children.Clear();
        _selection = null;
        if (g is null) return;
        foreach (var n in g.Noeuds) AjouterVueNoeud(n);
        foreach (var l in g.Liens) AjouterLien(l);
    }

    public void Selectionner(Noeud? n)
    {
        _selection = n;
        foreach (var kv in _vuesNoeuds)
        {
            kv.Value.Opacity = (n is null || kv.Value.Noeud == n) ? 1.0 : 0.5;
        }
    }

    public Noeud AjouterNoeud(Modele.Noeud n)
    {
        if (_graphe is null) throw new InvalidOperationException("aucun graphe charge");
        if (!_graphe.Noeuds.Contains(n))
        {
            SenSÉ.Atelier.Modele.Historique.Pousser(_graphe);
            _graphe.Noeuds.Add(n);
        }
        AjouterVueNoeud(n);
        GrapheModifie?.Invoke();
        return n;
    }

    public Lien CreerLien(Noeud src, string portSrc, Noeud cbl, string portCbl)
    {
        if (_graphe is null) throw new InvalidOperationException("aucun graphe charge");
        if (!_vuesNoeuds.ContainsKey(src.Id) || !_vuesNoeuds.ContainsKey(cbl.Id))
            throw new InvalidOperationException("noeud absent du canvas");
        var srcPort = src.PortsSortie.FirstOrDefault(p => p.Nom == portSrc);
        var cblPort = cbl.PortsEntree.FirstOrDefault(p => p.Nom == portCbl);
        if (srcPort is null || cblPort is null) throw new InvalidOperationException("port introuvable");
        if (!srcPort.Type.Compatible(cblPort.Type)) throw new InvalidOperationException("types incompatibles");
        // Refuser les doublons
        if (_graphe.Liens.Any(l => l.NoeudSourceId == src.Id && l.PortSourceNom == portSrc
                                 && l.NoeudCibleId == cbl.Id && l.PortCibleNom == portCbl))
            throw new InvalidOperationException("lien deja existant");
        SenSÉ.Atelier.Modele.Historique.Pousser(_graphe);
        var lien = new Lien
        {
            NoeudSourceId = src.Id, PortSourceNom = portSrc,
            NoeudCibleId = cbl.Id, PortCibleNom = portCbl,
        };
        _graphe.Liens.Add(lien);
        AjouterLien(lien);
        GrapheModifie?.Invoke();
        return lien;
    }

    public void SupprimerNoeud(Noeud n)
    {
        if (_graphe is null) return;
        SenSÉ.Atelier.Modele.Historique.Pousser(_graphe);
        _graphe.Noeuds.RemoveAll(x => x.Id == n.Id);
        _graphe.Liens.RemoveAll(l => l.NoeudSourceId == n.Id || l.NoeudCibleId == n.Id);
        // Rafraichir le canvas
        ChargerGraphe(_graphe);
        GrapheModifie?.Invoke();
    }

    public void SupprimerLien(Lien l)
    {
        if (_graphe is null) return;
        SenSÉ.Atelier.Modele.Historique.Pousser(_graphe);
        _graphe.Liens.RemoveAll(x => x.Id == l.Id);
        ChargerGraphe(_graphe);
        GrapheModifie?.Invoke();
    }

    private void AjouterVueNoeud(Modele.Noeud n)
    {
        var v = new VueNoeud(n);
        v.Selectionnee += Noeud => { Selectionner(Noeud); SelectionNoeud?.Invoke(Noeud); };
        v.Deplacement += (noeud, dx, dy) => { DeplacerLien(noeud); NoeudDeplace?.Invoke(noeud, dx, dy); GrapheModifie?.Invoke(); };
        Surface.Children.Add(v);
        _vuesNoeuds[n.Id] = v;
    }

    private void AjouterLien(Lien l)
    {
        var src = _vuesNoeuds.GetValueOrDefault(l.NoeudSourceId);
        var cbl = _vuesNoeuds.GetValueOrDefault(l.NoeudCibleId);
        if (src is null || cbl is null) return;
        var path = new Path
        {
            Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8FB4FF")),
            StrokeThickness = 2,
            Data = new PathGeometry(),
            Tag = l,
        };
        Surface.Children.Insert(0, path);
        MettreAJourLien(l, path, src, cbl);
    }

    private void DeplacerLien(Modele.Noeud n)
    {
        if (_graphe is null) return;
        foreach (var l in _graphe.Liens)
        {
            if (l.NoeudSourceId == n.Id || l.NoeudCibleId == n.Id)
            {
                var path = Surface.Children.OfType<Path>().FirstOrDefault(p => (p.Tag as Lien)?.Id == l.Id);
                if (path is null) continue;
                var src = _vuesNoeuds.GetValueOrDefault(l.NoeudSourceId);
                var cbl = _vuesNoeuds.GetValueOrDefault(l.NoeudCibleId);
                if (src is not null && cbl is not null) MettreAJourLien(l, path, src, cbl);
            }
        }
    }

    private static void MettreAJourLien(Lien l, Path path, VueNoeud src, VueNoeud cbl)
    {
        var srcW = src.ActualWidth > 0 ? src.ActualWidth : 160;
        var srcH = src.ActualHeight > 0 ? src.ActualHeight : 80;
        var cblW = cbl.ActualWidth > 0 ? cbl.ActualWidth : 160;
        var cblH = cbl.ActualHeight > 0 ? cbl.ActualHeight : 80;
        var x1 = src.Noeud.X + srcW;
        var y1 = src.Noeud.Y + srcH / 2;
        var x2 = cbl.Noeud.X;
        var y2 = cbl.Noeud.Y + cblH / 2;
        var mx = (x1 + x2) / 2;
        var figure = new PathFigure { StartPoint = new Point(x1, y1), IsClosed = false };
        figure.Segments.Add(new BezierSegment(new Point(mx, y1), new Point(mx, y2), new Point(x2, y2), true));
        var geom = new PathGeometry();
        geom.Figures.Add(figure);
        path.Data = geom;
    }

    private void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Selectionner(null);
        SelectionNoeud?.Invoke(null!);
    }

    private void Surface_DragEnterOrOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(Palette.DragFormatNoeud))
            e.Effects = DragDropEffects.Copy;
        else if (e.Data.GetDataPresent(DragFormatPort))
            e.Effects = DragDropEffects.Link;
        else
            e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Surface_Drop(object sender, DragEventArgs e)
    {
        if (_graphe is null) { e.Handled = true; return; }
        // Cas 1 : drop d'un DefinitionNoeud depuis la palette
        if (e.Data.GetDataPresent(Palette.DragFormatNoeud))
        {
            var def = e.Data.GetData(Palette.DragFormatNoeud) as DefinitionNoeud;
            if (def is null) { e.Handled = true; return; }
            var p = e.GetPosition(Surface);
            // Si on drop sur un noeud, on decale un peu
            var n = new Modele.Noeud
            {
                Type = def.Id,
                X = Math.Max(0, p.X),
                Y = Math.Max(0, p.Y),
                PortsEntree = def.PortsEntree.ToList(),
                PortsSortie = def.PortsSortie.ToList(),
            };
            foreach (var pp in def.Params) n.Params[pp.Nom] = pp.Defaut;
            AjouterNoeud(n);
            Selectionner(n);
            SelectionNoeud?.Invoke(n);
            e.Handled = true;
        }
    }

    public void SauvegarderSiNecessaire() => GrapheModifie?.Invoke();
}