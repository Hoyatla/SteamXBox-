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
    /// <summary>Acces au Canvas interne (x:Name=Surface) pour RenderTargetBitmap.</summary>
    public Canvas? SurfaceCtl => Surface;

    private readonly System.Windows.Controls.Border _groupesContainer = new() { IsHitTestVisible = false };
    private readonly System.Windows.Controls.TextBlock _commentairesContainer = new() { IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap };
    public const string DragFormatPort = "Atelier.PortRef";

    private Graphe? _graphe;
    private readonly Dictionary<string, VueNoeud> _vuesNoeuds = new();
    private readonly List<Noeud> _selection = new();
    private Noeud? _focused;
    public IReadOnlyList<Noeud> Selection => _selection;

    public event Action<Noeud>? SelectionNoeud;
    public event Action<Noeud, double, double>? NoeudDeplace;
    public event Action? GrapheModifie;

    public CanvasAtelier()
    {
        InitializeComponent();
        Surface.MouseLeftButtonDown += Surface_MouseLeftButtonDown;
    }

    public Graphe? Graphe => _graphe;

    public void ChargerGraphe(Graphe? g)
    {
        _graphe = g;
        _vuesNoeuds.Clear();
        Surface.Children.Clear();
        _groupesContainer.Child = null;
        _commentairesContainer.Inlines.Clear();
        _selection.Clear();
        if (g is null) return;
        foreach (var n in g.Noeuds) AjouterVueNoeud(n);
        foreach (var l in g.Liens) AjouterLien(l);
        // Phase 3.5 : rendre les groupes
        if (g.Groupes is not null && g.Groupes.Count > 0)
        {
            var panel = new System.Windows.Controls.Canvas();
            foreach (var grp in g.Groupes)
            {
                if (grp.NoeudIds.Count == 0) continue;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (var nid in grp.NoeudIds)
                {
                    var n = g.Noeuds.FirstOrDefault(x => x.Id == nid);
                    if (n is null) continue;
                    if (n.X < minX) minX = n.X; if (n.Y < minY) minY = n.Y;
                    if (n.X + 140 > maxX) maxX = n.X + 140; if (n.Y + 90 > maxY) maxY = n.Y + 90;
                }
                if (minX > maxX) continue;
                var brush = grp.Couleur switch
                {
                    "red" => System.Windows.Media.Brushes.Red,
                    "orange" => System.Windows.Media.Brushes.Orange,
                    "yellow" => System.Windows.Media.Brushes.Goldenrod,
                    "green" => System.Windows.Media.Brushes.LimeGreen,
                    _ => System.Windows.Media.Brushes.DodgerBlue,
                };
                var bord = new System.Windows.Controls.Border
                {
                    BorderBrush = brush, BorderThickness = new System.Windows.Thickness(2),
                    CornerRadius = new System.Windows.CornerRadius(8),
                    Width = maxX - minX + 16, Height = maxY - minY + 16,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x22, brush.Color.R, brush.Color.G, brush.Color.B)),
                };
                System.Windows.Controls.Canvas.SetLeft(bord, minX - 8);
                System.Windows.Controls.Canvas.SetTop(bord, minY - 8);
                panel.Children.Add(bord);
            }
            System.Windows.Controls.Canvas.SetLeft(panel, 0);
            System.Windows.Controls.Canvas.SetTop(panel, 0);
            Surface.Children.Insert(0, panel);
        }
        // Phase 3.6 : rendre les commentaires
        if (g.Commentaires is not null && g.Commentaires.Count > 0)
        {
            foreach (var c in g.Commentaires)
            {
                var tb = new System.Windows.Controls.TextBlock
                {
                    Text = c.Texte,
                    FontSize = c.Taille,
                    Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(c.Couleur) is var col ? col : System.Windows.Media.Colors.Yellow),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x88, 0, 0, 0)),
                    Padding = new System.Windows.Thickness(6, 3, 6, 3),
                };
                System.Windows.Controls.Canvas.SetLeft(tb, c.X);
                System.Windows.Controls.Canvas.SetTop(tb, c.Y);
                Surface.Children.Add(tb);
            }
        }
    }

    public void Selectionner(Noeud? n, bool ajouter = false)
    {
        if (n is null && !ajouter)
        {
            ToutDeselectionner();
            return;
        }
        if (n is null) return;
        if (!ajouter) _selection.Clear();
        if (!_selection.Contains(n)) _selection.Add(n);
        foreach (var kv in _vuesNoeuds)
        {
            var estSel = _selection.Contains(kv.Value.Noeud);
            kv.Value.Opacity = estSel ? 1.0 : 0.5;
            kv.Value.DefinirSelection(estSel);
        }
        // Defocaliser l'ancien, focaliser le nouveau
        if (_focused is not null && _focused != n)
        {
            if (_vuesNoeuds.TryGetValue(_focused.Id, out var oldFoc))
                oldFoc.DefinirFocused(false);
        }
        _focused = n;
        if (_vuesNoeuds.TryGetValue(n.Id, out var newFoc))
            newFoc.DefinirFocused(true);
    }

    public void BasculerSelection(Noeud n)
    {
        if (_selection.Remove(n))
        {
            if (_vuesNoeuds.TryGetValue(n.Id, out var v))
            {
                v.Opacity = 0.5;
                v.DefinirSelection(false);
            }
        }
        else
        {
            _selection.Add(n);
            if (_vuesNoeuds.TryGetValue(n.Id, out var v))
            {
                v.Opacity = 1.0;
                v.DefinirSelection(true);
            }
        }
    }

    public void ToutSelectionner()
    {
        _selection.Clear();
        if (_graphe is null) return;
        foreach (var n in _graphe.Noeuds) _selection.Add(n);
        foreach (var kv in _vuesNoeuds)
        {
            kv.Value.Opacity = 1.0;
            kv.Value.DefinirSelection(true);
        }
    }

    public void ToutDeselectionner()
    {
        foreach (var n in _selection)
            if (_vuesNoeuds.TryGetValue(n.Id, out var v))
            {
                v.Opacity = 0.5;
                v.DefinirSelection(false);
            }
        _selection.Clear();
        if (_focused is not null && _vuesNoeuds.TryGetValue(_focused.Id, out var f))
            f.DefinirFocused(false);
        _focused = null;
        Keyboard.ClearFocus();
    }

    public void SupprimerSelection()
    {
        if (_graphe is null || _selection.Count == 0) return;
        // Confirmation si des liens attaches
        bool aDesLiens = false;
        foreach (var n in _selection)
            if (_graphe.Liens.Any(l => l.NoeudSourceId == n.Id || l.NoeudCibleId == n.Id))
            { aDesLiens = true; break; }
        if (aDesLiens)
        {
            var r = MessageBox.Show(
                "Supprimer " + _selection.Count + " nœud(s) et leurs liens ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }
        foreach (var n in _selection.ToList()) SupprimerNoeud(n);
        ToutDeselectionner();
        GrapheModifie?.Invoke();
    }

    public Noeud? DupliquerSelection()
    {
        if (_graphe is null || _selection.Count == 0) return null;
        var src = _selection[0];
        var def = CatalogueNoeuds.Trouver(src.Type);
        if (def is null) return null;
        var dup = new Modele.Noeud
        {
            Type = src.Type,
            X = src.X + 30, Y = src.Y + 30,
            PortsEntree = def.PortsEntree.ToList(),
            PortsSortie = def.PortsSortie.ToList(),
        };
        foreach (var kv in src.Params) dup.Params[kv.Key] = kv.Value;
        AjouterNoeud(dup);
        Selectionner(dup);
        return dup;
    }

    public void NaviguerFleche(Key key)
    {
        if (_graphe is null || _vuesNoeuds.Count == 0) return;
        Noeud? cur = _focused ?? (_selection.Count > 0 ? _selection[0] : _graphe.Noeuds.FirstOrDefault());
        if (cur is null) return;
        var cx = cur.X + 80; // centre approx
        var cy = cur.Y + 40;
        Noeud? best = null;
        double bestScore = double.MaxValue;
        foreach (var n in _graphe.Noeuds)
        {
            if (n == cur) continue;
            var nx = n.X + 80;
            var ny = n.Y + 40;
            var dx = nx - cx;
            var dy = ny - cy;
            bool ok = key switch
            {
                Key.Right => dx > 0 && Math.Abs(dy) < Math.Abs(dx) * 1.5,
                Key.Left => dx < 0 && Math.Abs(dy) < Math.Abs(-dx) * 1.5,
                Key.Down => dy > 0 && Math.Abs(dx) < Math.Abs(dy) * 1.5,
                Key.Up => dy < 0 && Math.Abs(dx) < Math.Abs(-dy) * 1.5,
                _ => false,
            };
            if (!ok) continue;
            double score = Math.Sqrt(dx * dx + dy * dy);
            if (score < bestScore) { bestScore = score; best = n; }
        }
        if (best is not null)
        {
            Selectionner(best);
            if (_vuesNoeuds.TryGetValue(best.Id, out var v)) v.Focus();
        }
    }

    public void NaviguerTab(bool reverse)
    {
        if (_graphe is null || _vuesNoeuds.Count == 0) return;
        var ordre = _graphe.Noeuds.OrderBy(n => n.Y).ThenBy(n => n.X).ToList();
        if (ordre.Count == 0) return;
        int idx = -1;
        if (_focused is not null) idx = ordre.IndexOf(_focused);
        int next = reverse ? (idx <= 0 ? ordre.Count - 1 : idx - 1)
                          : (idx < 0 ? 0 : (idx + 1) % ordre.Count);
        var cible = ordre[next];
        Selectionner(cible);
        if (_vuesNoeuds.TryGetValue(cible.Id, out var v)) v.Focus();
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
        v.Selectionnee += Noeud =>
        {
            var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (ctrl) BasculerSelection(Noeud);
            else if (shift) Selectionner(Noeud, ajouter: true);
            else Selectionner(Noeud);
            SelectionNoeud?.Invoke(Noeud);
        };
        v.NoeudTouche += (n, ke) =>
        {
            if (ke.Key is Key.Enter or Key.Space)
            {
                Selectionner(n);
                ke.Handled = true;
            }
        };
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
        if (e.OriginalSource != Surface) return; // clic sur un enfant (noeud ou lien) -> ne pas deselectionner
        ToutDeselectionner();
        SelectionNoeud?.Invoke(null!);
    }

    private void Surface_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Suppr / Backspace : supprimer la selection
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            SupprimerSelection();
            e.Handled = true;
            return;
        }
        // Ctrl+A : tout selectionner
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToutSelectionner();
            e.Handled = true;
            return;
        }
        // Ctrl+D : dupliquer
        if (e.Key == Key.D && Keyboard.Modifiers == ModifierKeys.Control)
        {
            DupliquerSelection();
            e.Handled = true;
            return;
        }
        // Tab/Shift+Tab : navigation entre noeuds
        if (e.Key == Key.Tab)
        {
            NaviguerTab((Keyboard.Modifiers & ModifierKeys.Shift) != 0);
            e.Handled = true;
            return;
        }
        // Fleches : navigation par position
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            NaviguerFleche(e.Key);
            e.Handled = true;
            return;
        }
        // Escape : deselectionner
        if (e.Key == Key.Escape)
        {
            ToutDeselectionner();
            SelectionNoeud?.Invoke(null!);
            e.Handled = true;
            return;
        }
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

    /// <summary>
    /// Met a jour le statut d'execution d'un noeud identifie par son id.
    /// Si le noeud n'est pas dans le canvas, l'appel est ignore (no-op).
    /// Utilise par FenetreAtelier pour suivre une execution en cours via le
    /// verbe HTTP /atelier/execution/etat.
    /// </summary>
    public void DefinirStatutNoeud(string noeudId, StatutExecution statut, string? erreur = null)
    {
        if (_vuesNoeuds.TryGetValue(noeudId, out var v))
        {
            v.DefinirStatutExecution(statut, erreur);
        }
    }

    /// <summary>
    /// Remet tous les noeuds a "non execute" (statut = null). Appele au debut
    /// d'une nouvelle execution pour effacer l'etat de la precedente.
    /// </summary>
    public void ReinitialiserStatuts()
    {
        foreach (var kv in _vuesNoeuds)
        {
            kv.Value.DefinirStatutExecution(null, null);
        }
    }

    /// <summary>Surligne (bordure jaune) tous les noeuds dont le type,
    /// le label vulgarise ou les valeurs de params contiennent <paramref name="terme"/>.
    /// Renvoie le nombre de noeuds surlignes.</summary>
    public int SurlignerRecherche(string terme, bool casse)
    {
        var cmp = casse ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int n = 0;
        foreach (var kv in _vuesNoeuds)
        {
            var noeud = kv.Value.Noeud;
            var def = CatalogueNoeuds.Trouver(noeud.Type);
            var label = def?.NomAffichage ?? noeud.Type;
            bool match = (label?.Contains(terme, cmp) ?? false)
                || (noeud.Type?.Contains(terme, cmp) ?? false);
            if (!match)
            {
                foreach (var p in noeud.Params)
                {
                    var sval = p.Value?.ToString() ?? "";
                    if (sval.Contains(terme, cmp)) { match = true; break; }
                }
            }
            kv.Value.DefinirMatch(match);
            if (match) n++;
        }
        return n;
    }

    public void EffacerSurlignageRecherche()
    {
        foreach (var kv in _vuesNoeuds) kv.Value.DefinirMatch(false);
    }

    /// <summary>Dans tous les noeuds, remplace <paramref name="terme"/> par
    /// <paramref name="remplacement"/> dans les valeurs de params (string).
    /// Renvoie le nombre total d'occurrences remplacees.</summary>
    public int RemplacerDansNoeuds(string terme, string remplacement, bool casse)
    {
        var cmp = casse ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int total = 0;
        if (_graphe is null) return 0;
        foreach (var noeud in _graphe.Noeuds)
        {
            foreach (var cle in noeud.Params.Keys.ToList())
            {
                var v = noeud.Params[cle]?.ToString();
                if (string.IsNullOrEmpty(v)) continue;
                if (v.Contains(terme, cmp))
                {
                    var nv = v.Replace(cmp == StringComparison.Ordinal ? terme : terme, remplacement);
                    noeud.Params[cle] = nv;
                    total += CompteOccurrences(v, terme, casse);
                }
            }
        }
        return total;
    }

    private static int CompteOccurrences(string source, string sub, bool casse)
    {
        if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(source)) return 0;
        var cmp = casse ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        int n = 0, i = 0;
        while ((i = source.IndexOf(sub, i, cmp)) >= 0) { n++; i += sub.Length; }
        return n;
    }


    /// <summary>Calcule la bounding box (en coords du Surface interne) qui
    /// englobe toutes les VueNoeud du graphe. Renvoie Empty si pas de noeud.</summary>
    public Rect ObtenirBordContenu()
    {
        if (_vuesNoeuds.Count == 0) return Rect.Empty;
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var v in _vuesNoeuds.Values)
        {
            var x = Canvas.GetLeft(v); var y = Canvas.GetTop(v);
            var w = v.ActualWidth; var h = v.ActualHeight;
            if (double.IsNaN(w) || w <= 0) w = 140;
            if (double.IsNaN(h) || h <= 0) h = 90;
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w; if (y + h > maxY) maxY = y + h;
        }
        return new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY));
    }


    private double _zoom = 1.0;
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = System.Math.Clamp(value, 0.25, 4.0);
            if (Surface is not null) Surface.RenderTransform = new System.Windows.Media.ScaleTransform(_zoom, _zoom);
            ZoomChanged?.Invoke(_zoom);
        }
    }
    public event System.Action<double>? ZoomChanged;
    public System.Windows.Rect GetViewportRect()
    {
        if (Scroller is null) return new System.Windows.Rect(0, 0, ActualWidth, ActualHeight);
        return new System.Windows.Rect(Scroller.HorizontalOffset, Scroller.VerticalOffset, ActualWidth, ActualHeight);
    }
    private void CanvasAtelier_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.Control) return;
        var facteur = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
        Zoom = Zoom * facteur;
        e.Handled = true;
    }
    private void CanvasAtelier_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.D0 && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            Zoom = 1.0;
            e.Handled = true;
        }
    }

}
