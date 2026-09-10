using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace SenSÉ.EditeurTexte.UI;

/// <summary>
/// Fenetre de recherche/remplacement. Phase G.1a : coquille UI + ouverture/fermeture,
/// la logique de recherche est branchee en G.4.
/// </summary>
public partial class FenetreRecherche : Window
{
    private readonly RichTextBox _rtb;
    private bool _modeRemplacement;

    public bool ModeRemplacement
    {
        get => _modeRemplacement;
        set
        {
            _modeRemplacement = value;
            LabelRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            ChampRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            BtnRemplacerUn.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            BtnToutRemplacer.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public FenetreRecherche(RichTextBox rtb)
    {
        InitializeComponent();
        _rtb = rtb;
        ModeRemplacement = false;

        BtnSuivant.Click       += (_, _) => Rechercher(avant: false);
        BtnPrecedent.Click     += (_, _) => Rechercher(avant: true);
        BtnRemplacerUn.Click   += (_, _) => RemplacerUn();
        BtnToutRemplacer.Click += (_, _) => RemplacerTout();
        BtnFermer.Click        += (_, _) => Close();

        // Phase G.4 : la touche Entree declenche Suivant, Echap declenche Fermer (deja par IsCancel).
        ChampRecherche.KeyDown += (_, e) => { if (e.Key == Key.Enter) Rechercher(avant: false); };
        ChampRemplacer.KeyDown += (_, e) => { if (e.Key == Key.Enter) RemplacerUn(); };
    }

    // Phase G.4 : implementation complete. En G.1a on laisse un message dans la
    // status bar parente pour signaler que la recherche est en construction.
    private void Rechercher(bool avant)
    {
        var main = Owner as MainWindow;
        if (main is not null) main.Statut.Text = "Recherche : implementation Phase G.4 (apres toolbar).";
    }

    private void RemplacerUn()
    {
        var main = Owner as MainWindow;
        if (main is not null) main.Statut.Text = "Remplacer : implementation Phase G.4.";
    }

    private void RemplacerTout()
    {
        var main = Owner as MainWindow;
        if (main is not null) main.Statut.Text = "Tout remplacer : implementation Phase G.4.";
    }
}
