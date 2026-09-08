using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SenSÉ.Atelier.UI;

/// <summary>
/// Barre de recherche/replace compacte, affichee en haut a droite du canvas.
/// Deux modes : Description (filtre le contenu de l'Inspecteur) et Canvas
/// (surligne les noeuds dont le label/les params contiennent le terme).
/// </summary>
public partial class SearchBar : UserControl
{
    /// <summary>Declenche quand l'utilisateur lance une recherche.
    /// Le handler externe fait le boulot, la SearchBar ne touche pas au canvas.</summary>
    public event Action<string, SearchMode, bool>? RechercheDemandee;
    /// <summary>Declenche quand l'utilisateur demande un remplacement global.</summary>
    public event Action<string, string, SearchMode, bool>? RemplacerToutDemande;
    /// <summary>Declenche quand l'utilisateur ferme la barre.</summary>
    public event EventHandler? FermerDemandee;

    public enum SearchMode { Description, Canvas }

    public SearchMode Mode { get; private set; } = SearchMode.Description;

    public string Terme => ZoneRecherche.Text ?? "";
    public string Remplacement => ZoneRemplacer.Text ?? "";
    public bool SensibleCasse => ChkCasse.IsChecked == true;

    public SearchBar()
    {
        InitializeComponent();
        ZoneRecherche.Focus();
    }

    public void DefinirMode(SearchMode mode)
    {
        Mode = mode;
        BtnModeDesc.Style = (Style)FindResource(mode == SearchMode.Description ? "BtnSrchActif" : "BtnSrch");
        BtnModeCanvas.Style = (Style)FindResource(mode == SearchMode.Canvas ? "BtnSrchActif" : "BtnSrch");
    }

    public void AfficherStatus(string message)
    {
        Status.Text = message ?? "";
    }

    public void FocusTerme()
    {
        ZoneRecherche.Focus();
        ZoneRecherche.SelectAll();
    }

    private void BtnModeDesc_Click(object sender, RoutedEventArgs e) => DefinirMode(SearchMode.Description);
    private void BtnModeCanvas_Click(object sender, RoutedEventArgs e) => DefinirMode(SearchMode.Canvas);

    private void BtnRechercher_Click(object sender, RoutedEventArgs e)
    {
        RechercheDemandee?.Invoke(Terme, Mode, SensibleCasse);
    }

    private void BtnRemplacerTout_Click(object sender, RoutedEventArgs e)
    {
        RemplacerToutDemande?.Invoke(Terme, Remplacement, Mode, SensibleCasse);
    }

    private void BtnFermer_Click(object sender, RoutedEventArgs e)
    {
        FermerDemandee?.Invoke(this, EventArgs.Empty);
    }

    private void ZoneRecherche_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) RechercheDemandee?.Invoke(Terme, Mode, SensibleCasse);
        else if (e.Key == Key.Escape) FermerDemandee?.Invoke(this, EventArgs.Empty);
    }
}
