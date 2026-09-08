using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Custom;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;
using SenSÉ.Atelier.Serialisation;

namespace SenSÉ.Atelier.UI;

public partial class FenetreAtelier : Window
{
    private readonly Persistance _persistance;
    private readonly ServeurHttp _serveur;
    private Graphe? _grapheActif;
    private readonly List<TabGraphe> _onglets = new();

    public ICommand AnnulerCmd { get; }
    public ICommand RefaireCmd { get; }
    public ICommand ExecuterCmd { get; }
    public ICommand SauvegarderCmd { get; }
    public ICommand SauvegarderSousCmd { get; }
    public ICommand NouveauGrapheCmd { get; }
    public ICommand FermerOngletCmd { get; }
    public ICommand OngletSuivantCmd { get; }
    public ICommand OngletPrecedentCmd { get; }
    public ICommand FocusPaletteCmd { get; }

    public FenetreAtelier(string racine, ServeurHttp serveur)
    {
        InitializeComponent();
        _persistance = new Persistance(racine);
        _serveur = serveur;
        CatalogueNoeuds.InitialiserSiNecessaire();
        CatalogueCustom.Recharger(racine);
        if (Application.Current is App app) app.EspaceCourant = Espace.Codage;
        RafraichirOnglets();
        SelectionnerGraphe(_onglets.FirstOrDefault()?.Graphe);
        PreviewKeyDown += Fenetre_PreviewKeyDown;
        ExecuterCmd = new RelayCommand(_ => ExecuterGraphe());
        SauvegarderCmd = new RelayCommand(_ => SauvegarderAction());
        SauvegarderSousCmd = new RelayCommand(_ => SauvegarderSousAction());
        NouveauGrapheCmd = new RelayCommand(_ => BtnNouveauGraphe_Click(this, new RoutedEventArgs()));
        FermerOngletCmd = new RelayCommand(_ => FermerOngletAction(), _ => _grapheActif is not null);
        OngletSuivantCmd = new RelayCommand(_ => OngletSuivant());
        OngletPrecedentCmd = new RelayCommand(_ => OngletPrecedent());
        FocusPaletteCmd = new RelayCommand(_ => PaletteCtl.FocusFiltre());
        AnnulerCmd = new RelayCommand(_ => AnnulerAction(), _ => PeutAnnuler());
        RefaireCmd = new RelayCommand(_ => RefaireAction(), _ => PeutRefaire());
        DataContext = this;
        MajBoutonsUndo();
    }

    private void RafraichirOnglets()
    {
        _onglets.Clear();
        foreach (var t in _persistance.ListerGraphes())
        {
            var g = _persistance.ChargerGraphe(t.id, t.espace);
            if (g is not null) _onglets.Add(new TabGraphe { Id = g.Id, Nom = g.Nom, Graphe = g });
        }
        if (_onglets.Count == 0)
        {
            foreach (var e in new[] { Espace.Codage, Espace.Multimedia })
            {
                var g = new Graphe { Espace = e, Nom = e == Espace.Codage ? "Mon premier code" : "Mon premier média" };
                _persistance.SauvegarderGraphe(g);
                _onglets.Add(new TabGraphe { Id = g.Id, Nom = g.Nom, Graphe = g });
            }
        }
        OngletsGraphes.ItemsSource = _onglets;
    }

    private void SelectionnerGraphe(Graphe? g)
    {
        _grapheActif = g;
        CanvasCtl.ChargerGraphe(g);
        InspecteurCtl.Vider();
        RafraichirBoutonsEspace();
        MajBoutonsUndo();
    }

    private void RafraichirBoutonsEspace()
    {
        var esp = _grapheActif?.Espace ?? Espace.Codage;
        BtnCodage.Style = (Style)(esp == Espace.Codage ? FindResource("BoutonAccent") : (Style)Application.Current.Resources[typeof(Button)]);
        BtnMultimedia.Style = (Style)(esp == Espace.Multimedia ? FindResource("BoutonAccent") : (Style)Application.Current.Resources[typeof(Button)]);
        PaletteCtl.AppliquerEspace(esp);
    }

    private void BtnCodage_Click(object sender, RoutedEventArgs e) => ChangerEspace(Espace.Codage);
    private void BtnMultimedia_Click(object sender, RoutedEventArgs e) => ChangerEspace(Espace.Multimedia);
    private void ChangerEspace(Espace e)
    {
        var g = _onglets.FirstOrDefault(o => o.Graphe.Espace == e)?.Graphe;
        if (g is null)
        {
            g = new Graphe { Espace = e, Nom = e == Espace.Codage ? "Nouveau code" : "Nouveau média" };
            _persistance.SauvegarderGraphe(g);
            _onglets.Add(new TabGraphe { Id = g.Id, Nom = g.Nom, Graphe = g });
            OngletsGraphes.ItemsSource = null;
            OngletsGraphes.ItemsSource = _onglets;
        }
        SelectionnerGraphe(g);
    }

    private void OngletGraphe_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string id)
        {
            var t = _onglets.FirstOrDefault(o => o.Id == id);
            if (t is not null) SelectionnerGraphe(t.Graphe);
        }
    }

    private void BtnNouveauGraphe_Click(object sender, RoutedEventArgs e)
    {
        var esp = _grapheActif?.Espace ?? Espace.Codage;
        var g = new Graphe { Espace = esp, Nom = "Nouveau graphe" };
        _persistance.SauvegarderGraphe(g);
        _onglets.Add(new TabGraphe { Id = g.Id, Nom = g.Nom, Graphe = g });
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
        SelectionnerGraphe(g);
    }

    private void Palette_NoeudChoisi(DefinitionNoeud def)
    {
        if (_grapheActif is null) return;
        var n = new Noeud
        {
            Type = def.Id,
            X = 100 + _grapheActif.Noeuds.Count * 30,
            Y = 100 + _grapheActif.Noeuds.Count * 30,
            PortsEntree = def.PortsEntree.ToList(),
            PortsSortie = def.PortsSortie.ToList(),
        };
        foreach (var p in def.Params)
            n.Params[p.Nom] = p.Defaut;
        CanvasCtl.AjouterNoeud(n);
        InspecteurCtl.Afficher(n);
        CanvasCtl.Selectionner(n);
    }

    private void CanvasCtl_SelectionNoeud(Noeud n)
    {
        if (n is null) InspecteurCtl.Vider();
        else InspecteurCtl.Afficher(n);
    }

    private void CanvasCtl_NoeudDeplace(Noeud n, double dx, double dy)
    {
        if (_grapheActif is not null) _persistance.SauvegarderGraphe(_grapheActif);
    }

    private void CanvasCtl_GrapheModifie()
    {
        if (_grapheActif is not null) _persistance.SauvegarderGraphe(_grapheActif);
        MajBoutonsUndo();
    }

    private void BtnExecuter_Click(object sender, RoutedEventArgs e) => ExecuterGraphe();
    private void ExecuterGraphe()
    {
        if (_grapheActif is null) return;
        Statut.Text = "Exécution en cours...";
        var exec = Moteur.Instance.LancerAsync(_grapheActif);
        Task.Run(async () =>
        {
            while (exec.Statut == StatutExecution.EnAttente || exec.Statut == StatutExecution.EnCours)
                await Task.Delay(200);
            Dispatcher.Invoke(() =>
            {
                Statut.Text = exec.Statut switch
                {
                    StatutExecution.Reussi => "✓ Terminé",
                    StatutExecution.Echec => "✗ " + (exec.Erreur ?? "echec"),
                    StatutExecution.Annule => "Annulé",
                    _ => "?",
                };
            });
        });
    }

    private void BtnSauvegarder_Click(object sender, RoutedEventArgs e)
    {
        if (_grapheActif is not null)
        {
            _persistance.SauvegarderGraphe(_grapheActif);
            StatutBas.Text = $"Sauvegardé à {DateTime.Now:HH:mm:ss} ({_grapheActif.Noeuds.Count} nœuds, {_grapheActif.Liens.Count} liens)";
        }
    }

    private bool PeutAnnuler() => _grapheActif is not null && Historique.PeutAnnuler(_grapheActif.Id);
    private bool PeutRefaire() => _grapheActif is not null && Historique.PeutRefaire(_grapheActif.Id);

    private void MajBoutonsUndo()
    {
        BtnAnnuler.IsEnabled = PeutAnnuler();
        BtnRefaire.IsEnabled = PeutRefaire();
    }

    private void AnnulerAction()
    {
        if (_grapheActif is null) return;
        if (!Historique.Annuler(_grapheActif)) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        CanvasCtl.ChargerGraphe(_grapheActif);
        StatutBas.Text = "Annulé.";
        MajBoutonsUndo();
    }

    private void RefaireAction()
    {
        if (_grapheActif is null) return;
        if (!Historique.Refaire(_grapheActif)) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        CanvasCtl.ChargerGraphe(_grapheActif);
        StatutBas.Text = "Refait.";
        MajBoutonsUndo();
    }

    private void BtnAnnuler_Click(object sender, RoutedEventArgs e) => AnnulerAction();
    private void BtnRefaire_Click(object sender, RoutedEventArgs e) => RefaireAction();

    private void SauvegarderAction()
    {
        if (_grapheActif is null) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        StatutBas.Text = $"Sauvegardé à {DateTime.Now:HH:mm:ss} ({_grapheActif.Noeuds.Count} nœuds, {_grapheActif.Liens.Count} liens)";
    }

    private void SauvegarderSousAction()
    {
        if (_grapheActif is null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Sauvegarder sous",
            Filter = "Graphe SenSÉ (*.json)|*.json|Tous les fichiers|*.*",
            FileName = _grapheActif.Nom + ".json",
        };
        if (dlg.ShowDialog(this) == true)
        {
            _persistance.ExporterGraphe(_grapheActif, dlg.FileName);
            StatutBas.Text = "Exporté vers " + dlg.FileName;
        }
    }

    private void FermerOngletAction()
    {
        if (_grapheActif is null) return;
        var r = MessageBox.Show(
            "Fermer le graphe \"" + _grapheActif.Nom + "\" ?",
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        var id = _grapheActif.Id;
        _onglets.RemoveAll(o => o.Id == id);
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
        SelectionnerGraphe(_onglets.FirstOrDefault()?.Graphe);
    }

    private void OngletSuivant()
    {
        if (_onglets.Count < 2 || _grapheActif is null) return;
        int idx = _onglets.FindIndex(o => o.Id == _grapheActif.Id);
        int next = (idx + 1) % _onglets.Count;
        SelectionnerGraphe(_onglets[next].Graphe);
    }

    private void OngletPrecedent()
    {
        if (_onglets.Count < 2 || _grapheActif is null) return;
        int idx = _onglets.FindIndex(o => o.Id == _grapheActif.Id);
        int prev = (idx - 1 + _onglets.Count) % _onglets.Count;
        SelectionnerGraphe(_onglets[prev].Graphe);
    }

    private void Fenetre_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Escape : deselection globale + fermer le focus palette
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Si le focus est dans la palette, le vider
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox
                && System.Windows.Input.Keyboard.FocusedElement == FindName("Filtre"))
            {
                // le filtre s'auto-vide (handler Filtre_KeyDown)
                return;
            }
            CanvasCtl.ToutDeselectionner();
            InspecteurCtl.Vider();
            StatutBas.Text = "Désélectionné.";
            e.Handled = true;
            return;
        }
        // Suppr/Backspace : supprimer la selection
        if (e.Key == System.Windows.Input.Key.Delete || e.Key == System.Windows.Input.Key.Back)
        {
            if (CanvasCtl.Selection.Count > 0)
            {
                CanvasCtl.SupprimerSelection();
                e.Handled = true;
            }
            return;
        }
        // Ctrl+D : dupliquer
        if (e.Key == System.Windows.Input.Key.D && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            if (CanvasCtl.Selection.Count > 0)
            {
                CanvasCtl.DupliquerSelection();
                e.Handled = true;
            }
            return;
        }
    }

    private void ExecuterAction_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Hook pour F5 si la command line ne se declenche pas
    }

    private class TabGraphe
    {
        public string Id { get; set; } = "";
        public string Nom { get; set; } = "";
        public Graphe Graphe { get; set; } = null!;
    }
}


internal sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _exec;
    private readonly Func<object?, bool>? _can;
    public RelayCommand(Action<object?> exec, Func<object?, bool>? can = null) { _exec = exec; _can = can; }
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _exec(parameter);
}