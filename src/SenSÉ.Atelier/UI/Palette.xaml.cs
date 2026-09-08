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
    public const string DragFormatNoeud = "Atelier.NoeudDef";

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

    public void FocusFiltre()
    {
        Filtre.Focus();
        Filtre.SelectAll();
    }

    private void Filtre_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (System.Windows.Data.CollectionViewSource.GetDefaultView(Liste.ItemsSource) is System.Windows.Data.ListCollectionView view)
        {
            var filtre = Filtre.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(filtre)) view.Filter = null;
            else
            {
                var f = filtre.ToLowerInvariant();
                view.Filter = o =>
                {
                    if (o is not PaletteItem pi) return false;
                    return (pi.Nom ?? "").ToLowerInvariant().Contains(f)
                        || (pi.Description ?? "").ToLowerInvariant().Contains(f);
                };
            }
        }
    }

    private void Filtre_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            Filtre.Text = "";
            Keyboard.ClearFocus();
            e.Handled = true;
            return;
        }
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AjouterItemSelectionneAuCentre();
            e.Handled = true;
        }
    }

    private void Liste_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            AjouterItemSelectionneAuCentre();
            e.Handled = true;
        }
    }

    private void Liste_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        AjouterItemSelectionneAuCentre();
    }

    private void AjouterItemSelectionneAuCentre()
    {
        if (Liste.SelectedItem is PaletteItem pi && pi.Def is not null)
        {
            NoeudChoisi?.Invoke(pi.Def);
        }
    }

    public void AppliquerEspace(Espace e)
    {
        var types = CatalogueNoeuds.ParEspace(e).ToList();
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
        // Si un drag vient d'avoir lieu, on ignore le click (sinon double creation)
        if (e.Handled) return;
        if (sender is FrameworkElement fe && fe.DataContext is PaletteItem pi)
        {
            NoeudChoisi?.Invoke(pi.Def);
        }
    }

    private void Item_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PaletteItem pi) return;
        if (pi.Def is null) return;

        // Demarre un drag&drop natif WPF avec le DefinitionNoeud dans le DataObject.
        var data = new DataObject();
        data.SetData(DragFormatNoeud, pi.Def);
        try
        {
            DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            // En cas d'echec du drag, on fallback sur l'ajout via click
            System.Diagnostics.Debug.WriteLine("DragDrop echoue: " + ex.Message);
        }
    }

    private void BtnNouveauCustom_Click(object sender, RoutedEventArgs e)
    {
        var espace = Espace.Codage;
        if (Application.Current is App app) espace = app.EspaceCourant;
        var w = new FenetreNoeudCustom();
        w.Owner = Window.GetWindow(this);
        var ok = w.ShowDialog() == true;
        if (ok) AppliquerEspace(espace); // rafraichit la palette apres CatalogueCustom.Recharger
    }

    private class PaletteItem
    {
        public string Nom { get; set; } = "";
        public string Description { get; set; } = "";
        public string Categorie { get; set; } = "";
        public DefinitionNoeud Def { get; set; } = null!;
    }
}