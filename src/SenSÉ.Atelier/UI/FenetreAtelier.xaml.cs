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
        InputBindings.Add(new KeyBinding(new ExecuterAction(this), new KeyGesture(Key.F5)));
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
            // Creer un graphe par defaut par espace
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
        // Valeurs par defaut
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

    private class TabGraphe
    {
        public string Id { get; set; } = "";
        public string Nom { get; set; } = "";
        public Graphe Graphe { get; set; } = null!;
    }

    private class ExecuterAction : ICommand
    {
        private readonly FenetreAtelier _w;
        public ExecuterAction(FenetreAtelier w) { _w = w; }
        
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _w.ExecuterGraphe();
    }
}