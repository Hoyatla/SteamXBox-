using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
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
    public ICommand RenommerOngletCmd { get; }

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
        ExecuterCmd = new RelayCommand(_ => ExecuterGraphe(), _ => _grapheActif is not null);
        SauvegarderCmd = new RelayCommand(_ => SauvegarderAction(), _ => _grapheActif is not null);
        SauvegarderSousCmd = new RelayCommand(_ => SauvegarderSousAction(), _ => _grapheActif is not null);
        NouveauGrapheCmd = new RelayCommand(_ => BtnNouveauGraphe_Click(this, new RoutedEventArgs()));
        FermerOngletCmd = new RelayCommand(_ => FermerOngletParId(_grapheActif?.Id), _ => _grapheActif is not null);
        OngletSuivantCmd = new RelayCommand(_ => OngletSuivant());
        OngletPrecedentCmd = new RelayCommand(_ => OngletPrecedent());
        FocusPaletteCmd = new RelayCommand(_ => PaletteCtl.FocusFiltre());
        RenommerOngletCmd = new RelayCommand(_ => RenommerOngletActif(), _ => _grapheActif is not null);
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
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
    }

    private void SelectionnerGraphe(Graphe? g)
    {
        _grapheActif = g;
        CanvasCtl.ChargerGraphe(g);
        InspecteurCtl.Vider();
        CanvasVide.Visibility = g is null ? Visibility.Visible : Visibility.Collapsed;
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
        if (Application.Current is App app) app.EspaceCourant = e;
        var g = _onglets.FirstOrDefault(o => o.Graphe.Espace == e)?.Graphe;
        SelectionnerGraphe(g);
    }

    private DateTime _dernierClicOnglet = DateTime.MinValue;
private string? _idDernierClicOnglet;

private void Onglet_HandleClick(object sender, MouseButtonEventArgs e)
{
    if (sender is not FrameworkElement fe || fe.Tag is not string id) return;
    var maintenant = DateTime.UtcNow;
    if (_idDernierClicOnglet == id && (maintenant - _dernierClicOnglet).TotalMilliseconds < 350)
    {
        _dernierClicOnglet = DateTime.MinValue;
        _idDernierClicOnglet = null;
        RenommerOnglet(id);
        e.Handled = true;
        return;
    }
    _dernierClicOnglet = maintenant;
    _idDernierClicOnglet = id;
    var t = _onglets.FirstOrDefault(o => o.Id == id);
    if (t is not null) SelectionnerGraphe(t.Graphe);
}

private void OngletFermer_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.Tag is string id)
        {
            FermerOngletParId(id);
        }
    }

    private void FermerOngletParId(string? id)
    {
        if (id is null) return;
        var t = _onglets.FirstOrDefault(o => o.Id == id);
        if (t is null) return;
        var r = MessageBox.Show(
            "Fermer le graphe \"" + t.Nom + "\" ?",
            "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        // Si l'onglet actif est celui qu'on supprime, on selectionne autre chose apres
        var etaitActif = _grapheActif?.Id == id;
        _onglets.RemoveAll(o => o.Id == id);
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
        if (etaitActif)
        {
            SelectionnerGraphe(_onglets.FirstOrDefault()?.Graphe);
        }
    }

    private void OngletRenommer_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement fe && fe.Tag is string id)
        {
            RenommerOnglet(id);
        }
    }

    private void RenommerOngletActif()
    {
        if (_grapheActif is null) return;
        RenommerOnglet(_grapheActif.Id);
    }

    private void RenommerOnglet(string id)
    {
        var t = _onglets.FirstOrDefault(o => o.Id == id);
        if (t is null) return;
        var dlg = new FenetreRenommerOnglet(t.Nom) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            t.Nom = dlg.NomSaisi;
            t.Graphe.Nom = dlg.NomSaisi;
            t.Modifie = true;
            _persistance.SauvegarderGraphe(t.Graphe);
            t.Modifie = false; // sauvegarde donc propre
            OngletsGraphes.ItemsSource = null;
            OngletsGraphes.ItemsSource = _onglets;
            StatutBas.Text = "Renommé en \"" + t.Nom + "\".";
        }
    }

    private void BtnNouveauGraphe_Click(object sender, RoutedEventArgs e)
    {
        var esp = _grapheActif?.Espace
                  ?? (Application.Current is App app ? app.EspaceCourant : Espace.Codage);
        var nom = DemanderNomGraphe();
        if (nom is null) return;
        var g = new Graphe { Espace = esp, Nom = nom };
        _persistance.SauvegarderGraphe(g);
        _onglets.Add(new TabGraphe { Id = g.Id, Nom = g.Nom, Graphe = g });
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
        SelectionnerGraphe(g);
        // Proposer de le renommer tout de suite
        RenommerOnglet(g.Id);
    }

    private string? DemanderNomGraphe()
    {
        var dlg = new FenetreRenommerOnglet("Nouveau graphe") { Owner = this };
        return dlg.ShowDialog() == true ? dlg.NomSaisi : null;
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
        if (_grapheActif is null) return;
        MarquerModifie();
        _persistance.SauvegarderGraphe(_grapheActif);
    }

    private void CanvasCtl_GrapheModifie()
    {
        if (_grapheActif is null) return;
        MarquerModifie();
        _persistance.SauvegarderGraphe(_grapheActif);
        MajBoutonsUndo();
    }

    private void MarquerModifie()
    {
        var t = _onglets.FirstOrDefault(o => o.Id == _grapheActif?.Id);
        if (t is null) return;
        t.Modifie = true;
        // Rafraichit l'affichage de l'onglet (le * apparait)
        OngletsGraphes.ItemsSource = null;
        OngletsGraphes.ItemsSource = _onglets;
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
        SauvegarderAction();
    }

    private void SauvegarderAction()
    {
        if (_grapheActif is null) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        var t = _onglets.FirstOrDefault(o => o.Id == _grapheActif.Id);
        if (t is not null)
        {
            t.Modifie = false;
            OngletsGraphes.ItemsSource = null;
            OngletsGraphes.ItemsSource = _onglets;
        }
        StatutBas.Text = $"Sauvegardé à {DateTime.Now:HH:mm:ss} ({_grapheActif.Noeuds.Count} nœuds, {_grapheActif.Liens.Count} liens)";
    }

    private bool PeutAnnuler() => _grapheActif is not null && Historique.PeutAnnuler(_grapheActif.Id);
    private bool PeutRefaire() => _grapheActif is not null && Historique.PeutRefaire(_grapheActif.Id);

    private void MajBoutonsUndo()
    {
        BtnAnnuler.IsEnabled = PeutAnnuler();
        BtnRefaire.IsEnabled = PeutRefaire();
        BtnExecuter.IsEnabled = _grapheActif is not null;
        BtnSauvegarder.IsEnabled = _grapheActif is not null;
        CommandManager.InvalidateRequerySuggested();
    }

    private void AnnulerAction()
    {
        if (_grapheActif is null) return;
        if (!Historique.Annuler(_grapheActif)) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        CanvasCtl.ChargerGraphe(_grapheActif);
        MarquerModifie();
        StatutBas.Text = "Annulé.";
        MajBoutonsUndo();
    }

    private void RefaireAction()
    {
        if (_grapheActif is null) return;
        if (!Historique.Refaire(_grapheActif)) return;
        _persistance.SauvegarderGraphe(_grapheActif);
        CanvasCtl.ChargerGraphe(_grapheActif);
        MarquerModifie();
        StatutBas.Text = "Refait.";
        MajBoutonsUndo();
    }

    private void BtnAnnuler_Click(object sender, RoutedEventArgs e) => AnnulerAction();
    private void BtnRefaire_Click(object sender, RoutedEventArgs e) => RefaireAction();

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
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox
                && System.Windows.Input.Keyboard.FocusedElement == FindName("Filtre"))
            {
                return;
            }
            CanvasCtl.ToutDeselectionner();
            InspecteurCtl.Vider();
            StatutBas.Text = "Désélectionné.";
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.Delete || e.Key == System.Windows.Input.Key.Back)
        {
            if (CanvasCtl.Selection.Count > 0)
            {
                CanvasCtl.SupprimerSelection();
                e.Handled = true;
            }
            return;
        }
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

    private class TabGraphe : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        private string _nom = "";
        public string Nom
        {
            get => _nom;
            set { _nom = value; OnPropertyChanged(nameof(Nom)); }
        }
        public Graphe Graphe { get; set; } = null!;
        private bool _modifie;
        public bool Modifie
        {
            get => _modifie;
            set { _modifie = value; OnPropertyChanged(nameof(Modifie)); OnPropertyChanged(nameof(MarqueurModifie)); }
        }
        public string MarqueurModifie => _modifie ? " *" : "";

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
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
