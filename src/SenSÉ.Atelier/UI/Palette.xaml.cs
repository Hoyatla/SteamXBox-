using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class Palette : UserControl
{
    public event Action<DefinitionNoeud>? NoeudChoisi;

    public Palette()
    {
        InitializeComponent();
        CatalogueNoeuds.InitialiserSiNecessaire();
        // Charger par espace courant
        Espace espace = Espace.Codage;
        if (Application.Current is App app) espace = app.EspaceCourant;
        AppliquerEspace(espace);
    }

    public void AppliquerEspace(Espace e)
    {
        var types = CatalogueNoeuds.ParEspace(e).ToList();
        var groupe = types.GroupBy(t => t.Categorie).OrderBy(g => g.Key);
        Liste.ItemsSource = groupe.SelectMany(g => g.Select(t => new { Nom = t.Nom, Description = t.Description, Def = t, Categorie = g.Key }))
            .ToList();
        // Regroupement : ItemsSource n'a pas de CollectionViewSource ici, on simplifie
        // en mettant les items sans groupement et en montrant la categorie dans le template
        var items = types.Select(t => new PaletteItem
        {
            Nom = t.Nom, Description = t.Description, Def = t, Categorie = t.Categorie,
        }).ToList();
        var view = new System.Windows.Data.ListCollectionView(items);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Categorie"));
        Liste.ItemsSource = view;
    }

    private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is PaletteItem pi)
        {
            NoeudChoisi?.Invoke(pi.Def);
        }
    }

    private class PaletteItem
    {
        public string Nom { get; set; } = "";
        public string Description { get; set; } = "";
        public string Categorie { get; set; } = "";
        public DefinitionNoeud Def { get; set; } = null!;
    }
}