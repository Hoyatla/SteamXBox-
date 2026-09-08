using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Text.Json.Nodes;
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

    public ICommand OuvrirSearchDescCmd { get; }
    public ICommand OuvrirSearchCanvasCmd { get; }
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
        // Phase 3.3 : wire MiniMap
        CanvasCtl.ZoomChanged += z => ZoomLabel.Text = "Zoom: " + (int)(z * 100) + "%";
        MettreAJourMiniMap();
        SelectionnerGraphe(_onglets.FirstOrDefault()?.Graphe);
        PreviewKeyDown += Fenetre_PreviewKeyDown;
        ExecuterCmd = new RelayCommand(_ => ExecuterGraphe(), _ => _grapheActif is not null);
        SauvegarderCmd = new RelayCommand(_ => SauvegarderAction(), _ => _grapheActif is not null);
        SauvegarderSousCmd = new RelayCommand(_ => SauvegarderSousAction(), _ => _grapheActif is not null);
        NouveauGrapheCmd = new RelayCommand(_ => BtnNouveauGraphe_Click(this, new RoutedEventArgs()));
        FermerOngletCmd = new RelayCommand(_ => FermerOngletParId(_grapheActif?.Id), _ => _grapheActif is not null);
        OngletSuivantCmd = new RelayCommand(_ => OngletSuivant());
        OngletPrecedentCmd = new RelayCommand(_ => OngletPrecedent());

        OuvrirSearchDescCmd = new RelayCommand(_ => OuvrirSearchBar(SearchBar.SearchMode.Description));
        OuvrirSearchCanvasCmd = new RelayCommand(_ => OuvrirSearchBar(SearchBar.SearchMode.Canvas));
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
        CanvasCtl.ChargerGraphe(g); MettreAJourMiniMap();
        InspecteurCtl.Vider();
        PanelVide.Visibility = g is null ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>
    /// Affiche un menu contextuel avec la liste des exemples charges depuis
    /// Outils/Atelier/Exemples/index.json (via le verbe HTTP /atelier/exemples/lister).
    /// Un clic sur un exemple appelle /atelier/exemples/charger et ouvre le
    /// nouveau graphe comme onglet.
    /// </summary>
    private void BtnChargerExemple_Click(object sender, RoutedEventArgs e)
    {
        if (_serveur is null) return;
        try
        {
            // Interroge le serveur HTTP pour la liste des exemples. Pas
            // d'equivalent local : la verite est sur disque, dans l'index.
            System.Text.Json.Nodes.JsonObject? obj = null;
            var resp = _serveur.AppelerVerbeSync("exemples/lister", null, null);
            if (resp is System.Text.Json.Nodes.JsonObject o1) obj = o1;
            if (obj is null || obj["ok"]?.GetValue<bool>() != true)
            {
                var err = obj?["error"]?.GetValue<string>() ?? "inconnu";
                MessageBox.Show("Impossible de lister les exemples : " + err,
                    "Atelier", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var exemples = obj["data"]?["exemples"] as System.Text.Json.Nodes.JsonArray;
            if (exemples is null || exemples.Count == 0)
            {
                MessageBox.Show("Aucun exemple trouve dans Outils/Atelier/Exemples/.",
                    "Atelier", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var menu = new ContextMenu();
            foreach (var item in exemples)
            {
                if (item is not System.Text.Json.Nodes.JsonObject jo) continue;
                var id = jo["id"]?.GetValue<string>() ?? "";
                var titre = jo["titre"]?.GetValue<string>() ?? id;
                var desc = jo["description"]?.GetValue<string>() ?? "";
                var mi = new MenuItem
                {
                    Header = titre,
                    ToolTip = desc,
                    Tag = id,
                };
                mi.Click += ChargerExempleMenuItem_Click;
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur chargement exemples : " + ex.Message,
                "Atelier", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ChargerExempleMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string exempleId) return;
        if (_serveur is null) return;
        try
        {
            var body = new System.Text.Json.Nodes.JsonObject
            {
                ["exemple_id"] = exempleId,
            };
            System.Text.Json.Nodes.JsonObject? obj = null;
            var resp = _serveur.AppelerVerbeSync("exemples/charger", body, null);
            if (resp is System.Text.Json.Nodes.JsonObject o2) obj = o2;
            if (obj is null || obj["ok"]?.GetValue<bool>() != true)
            {
                var err = obj?["error"]?.GetValue<string>() ?? "inconnu";
                MessageBox.Show("Impossible de charger l'exemple : " + err,
                    "Atelier", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // Rafraichit la liste d'onglets et selectionne le nouveau graphe.
            RafraichirOnglets();
            var gid = obj["data"]?["graphe_id"]?.GetValue<string>();
            if (gid is not null)
            {
                var t = _onglets.FirstOrDefault(o => o.Id == gid);
                if (t is not null) SelectionnerGraphe(t.Graphe);
            }
            StatutBas.Text = "Exemple charge.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur : " + ex.Message,
                "Atelier", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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

    // Memoire de l'execution courante (pour le bouton Annuler).
    private string? _executionEnCours;
    private CancellationTokenSource? _pollingCts;

    private void BtnExecuter_Click(object sender, RoutedEventArgs e) => ExecuterGraphe();

    private void ExecuterGraphe()
    {
        if (_grapheActif is null) return;
        if (_executionEnCours is not null)
        {
            Statut.Text = "Une execution est deja en cours.";
            return;
        }

        // Reset visuel : efface les statuts precedents et ouvre le panel.
        CanvasCtl.ReinitialiserStatuts();
        PanelExecution.Visibility = Visibility.Visible;
        TexteSorties.Text = "";
        ProgressionExec.Value = 0;
        TexteProgression.Text = "0 / 0 noeuds";
        Statut.Text = "Execution en cours...";

        // Lance l'execution via HTTP (le subprocess headless, ou l'in-process
        // si l'Atelier a ete demarre avec une fenetre). L'execution est
        // asynchrone cote moteur.
        var resp = _serveur.AppelerVerbeSync("executer",
            new JsonObject { ["graphe_id"] = JsonValue.Create(_grapheActif.Id) }, null);
        if (resp is not JsonObject obj || obj["ok"]?.GetValue<bool>() != true)
        {
            var err = resp?["error"]?.GetValue<string>() ?? "inconnu";
            Statut.Text = "Echec lancement : " + err;
            PanelExecution.Visibility = Visibility.Collapsed;
            return;
        }
        _executionEnCours = obj["data"]?["execution_id"]?.GetValue<string>();
        if (_executionEnCours is null)
        {
            Statut.Text = "execution_id absent de la reponse";
            PanelExecution.Visibility = Visibility.Collapsed;
            return;
        }

        // Poll l'etat toutes les 250ms jusqu'a terminaison.
        _pollingCts = new CancellationTokenSource();
        var token = _pollingCts.Token;
        Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var st = _serveur.AppelerVerbeSync("execution/etat", null,
                        new Dictionary<string, string> { ["execution_id"] = _executionEnCours });
                    Dispatcher.Invoke(() => MettreAJourUiExecution(st));
                    if (st is JsonObject stObj
                        && stObj["ok"]?.GetValue<bool>() == true
                        && stObj["data"]?["statut"]?.GetValue<string>() is { } s
                        && s != "EnCours" && s != "EnAttente")
                    {
                        break; // fini
                    }
                    await Task.Delay(250, token);
                }
            }
            catch (OperationCanceledException) { /* normal, on a demande l'arret */ }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Statut.Text = "Erreur polling : " + ex.Message);
            }
        }, token);
    }

    /// <summary>
    /// Met a jour la barre de progression, la mini-console, les bordures des
    /// noeuds, et le statut texte, d'apres la reponse JSON de /execution/etat.
    /// </summary>
    private void MettreAJourUiExecution(JsonObject? resp)
    {
        if (resp is null || resp["ok"]?.GetValue<bool>() != true) return;
        var data = resp["data"] as JsonObject;
        if (data is null) return;

        // Progression
        var noeuds = data["noeuds"] as JsonArray;
        var total = noeuds?.Count ?? 0;
        var reussis = 0;
        var enCours = 0;
        var enEchec = 0;
        if (noeuds is not null)
        {
            foreach (var n in noeuds)
            {
                if (n is not JsonObject no) continue;
                var st = no["statut"]?.GetValue<string>() ?? "";
                if (st == "Reussi") reussis++;
                else if (st == "EnCours") enCours++;
                else if (st == "Echec") enEchec++;

                // Met a jour la bordure du noeud correspondant
                var nid = no["noeud_id"]?.GetValue<string>();
                if (nid is null) continue;
                StatutExecution statut = st switch
                {
                    "EnAttente" => StatutExecution.EnAttente,
                    "EnCours"   => StatutExecution.EnCours,
                    "Reussi"    => StatutExecution.Reussi,
                    "Echec"     => StatutExecution.Echec,
                    "Annule"    => StatutExecution.Annule,
                    _ => StatutExecution.EnAttente,
                };
                var err = no["erreur"]?.GetValue<string>();
                CanvasCtl.DefinirStatutNoeud(nid, statut, err);
            }
        }
        ProgressionExec.Value = total > 0 ? (reussis + enEchec) * 100.0 / total : 0;
        TexteProgression.Text = reussis + " / " + total + " noeuds"
            + (enEchec > 0 ? " (" + enEchec + " en echec)" : "")
            + (enCours > 0 ? " (" + enCours + " en cours)" : "");

        // Mini-console : stdout/stderr des noeuds reussis et en erreur
        var sb = new System.Text.StringBuilder();
        if (noeuds is not null)
        {
            foreach (var n in noeuds)
            {
                if (n is not JsonObject no) continue;
                var type = no["type"]?.GetValue<string>() ?? "?";
                var sorties = no["sorties"] as JsonObject;
                if (sorties is null) continue;
                var stdout = sorties["stdout"]?.GetValue<string>();
                var stderr = sorties["stderr"]?.GetValue<string>();
                var codeRetour = sorties["code_retour"]?.GetValue<int?>();
                if (!string.IsNullOrEmpty(stdout))
                    sb.AppendLine("[" + type + "] stdout:");
                if (!string.IsNullOrEmpty(stdout))
                    sb.AppendLine(stdout);
                if (!string.IsNullOrEmpty(stderr))
                {
                    sb.AppendLine("[" + type + "] stderr:");
                    sb.AppendLine(stderr);
                }
            }
        }
        if (sb.Length > 0) TexteSorties.Text = sb.ToString();

        // Statut global
        var statutGlobal = data["statut"]?.GetValue<string>() ?? "?";
        Statut.Text = statutGlobal switch
        {
            "EnCours"   => "Execution en cours...",
            "Reussi"    => "✓ Termine",
            "Echec"     => "✗ " + (data["erreur"]?.GetValue<string>() ?? "echec"),
            "Annule"    => "Annule",
            _ => statutGlobal,
        };

        // Si fini, on coupe le polling et on remet l'UI en place
        if (statutGlobal is "Reussi" or "Echec" or "Annule")
        {
            _pollingCts?.Cancel();
            _executionEnCours = null;
            // Cache le panel apres 3 secondes pour un reussi propre
            if (statutGlobal == "Reussi")
            {
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    PanelExecution.Visibility = Visibility.Collapsed;
                };
                timer.Start();
            }
        }
    }

    private void BtnAnnulerExec_Click(object sender, RoutedEventArgs e)
    {
        if (_executionEnCours is null) return;
        _serveur.AppelerVerbeSync("execution/annuler",
            new JsonObject { ["execution_id"] = JsonValue.Create(_executionEnCours) }, null);
        // La reponse du polling terminera naturellement l'UI
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


    private void OuvrirSearchBar(SearchBar.SearchMode mode)
    {
        SearchCtl.DefinirMode(mode);
        SearchCtl.Visibility = Visibility.Visible;
        SearchCtl.FocusTerme();
        // S'abonne aux events de la SearchBar (idempotent : on retire avant)
        SearchCtl.RechercheDemandee -= EffectuerRecherche;
        SearchCtl.RechercheDemandee += EffectuerRecherche;
        SearchCtl.RemplacerToutDemande -= EffectuerRemplacerTout;
        SearchCtl.RemplacerToutDemande += EffectuerRemplacerTout;
        SearchCtl.FermerDemandee -= FermerSearchBar;
        SearchCtl.FermerDemandee += FermerSearchBar;
    }

    private void FermerSearchBar(object? sender, EventArgs e)
    {
        SearchCtl.Visibility = Visibility.Collapsed;
        CanvasCtl.EffacerSurlignageRecherche();
    }

    private void EffectuerRecherche(string terme, SearchBar.SearchMode mode, bool casse)
    {
        if (string.IsNullOrEmpty(terme))
        {
            SearchCtl.AfficherStatus("Vide.");
            CanvasCtl.EffacerSurlignageRecherche();
            return;
        }
        if (mode == SearchBar.SearchMode.Canvas)
        {
            var n = CanvasCtl.SurlignerRecherche(terme, casse);
            SearchCtl.AfficherStatus(n + " noeud(s) surligne(s).");

        }
        else
        {
            // Description : cherche dans le contenu des params du noeud selectionne
            var trouve = InspecteurCtl.SelectionnerTexteDansContenu(terme, casse);
            SearchCtl.AfficherStatus(trouve ? "Selectionne dans l\u2019inspecteur." : "Aucune correspondance dans l\u2019inspecteur.");
        }
    }

    private void EffectuerRemplacerTout(string terme, string remplacement, SearchBar.SearchMode mode, bool casse)
    {
        if (string.IsNullOrEmpty(terme))
        {
            SearchCtl.AfficherStatus("Vide.");
            return;
        }
        if (mode == SearchBar.SearchMode.Canvas)
        {
            var n = CanvasCtl.RemplacerDansNoeuds(terme, remplacement, casse);
            MarquerModifie();
            if (_grapheActif is not null) _persistance.SauvegarderGraphe(_grapheActif);
            SearchCtl.AfficherStatus(n + " occurrence(s) remplacee(s) dans les params.");
        }
        else
        {
            var n = InspecteurCtl.RemplacerDansContenuCourant(terme, remplacement, casse);
            SearchCtl.AfficherStatus(n + " occurrence(s) dans l\u2019inspecteur.");
        }
    }


    private void BtnExportPng_Click(object sender, RoutedEventArgs e)
    {
        if (_grapheActif is null) { StatutBas.Text = "Pas de graphe actif."; return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Image PNG|*.png",
            FileName = SanitizeNomFichier(_grapheActif.Nom) + ".png",
            Title = "Exporter le graphe en PNG",
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            // Bounding box + 50px de marge
            var bord = CanvasCtl.ObtenirBordContenu();
            double minX, minY, w, h;
            if (bord.IsEmpty)
            { minX = 0; minY = 0; w = 400; h = 300; }
            else { minX = bord.X; minY = bord.Y; w = bord.Width; h = bord.Height; }
            const double M = 50;
            var imgW = (int)Math.Ceiling(w + 2 * M);
            var imgH = (int)Math.Ceiling(h + 2 * M);
            // Surface interne du canvas (le Canvas dans Canvas.xaml)
            var surface = CanvasCtl.SurfaceCtl;
            if (surface is null) { StatutBas.Text = "Surface canvas introuvable."; return; }
            // Force un layout pour avoir les tailles a jour
            surface.UpdateLayout();
            // Dessine le canvas translate pour que le coin haut-gauche de la bbox tombe a (M, M)
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new System.Windows.Media.VisualBrush(surface)
                {
                    Stretch = System.Windows.Media.Stretch.None,
                    AlignmentX = System.Windows.Media.AlignmentX.Left,
                    AlignmentY = System.Windows.Media.AlignmentY.Top,
                };
                vb.Transform = new System.Windows.Media.MatrixTransform(1, 0, 0, 1, -minX + M, -minY + M);
                dc.DrawRectangle(vb, null, new System.Windows.Rect(0, 0, imgW, imgH));
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(imgW, imgH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = System.IO.File.Create(dlg.FileName))
                enc.Save(fs);
            StatutBas.Text = "Export PNG : " + dlg.FileName + " (" + imgW + "x" + imgH + ")";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Erreur export PNG : " + ex.Message, "Atelier",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string SanitizeNomFichier(string s)
    {
        var invalides = System.IO.Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s) sb.Append(System.Array.IndexOf(invalides, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private void MettreAJourMiniMap()
    {
        if (CanvasCtl is null || MiniMapCtl is null) return;
        MiniMapCtl.MettreAJour(_grapheActif, CanvasCtl.GetViewportRect());
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
