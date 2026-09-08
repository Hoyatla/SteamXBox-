using System.Windows;
using System.Windows.Input;

namespace SenSÉ.Atelier.UI;

public partial class FenetreRenommerOnglet : Window
{
    public string NomSaisi { get; private set; } = "";

    public FenetreRenommerOnglet(string nomInitial)
    {
        InitializeComponent();
        Champ.Text = nomInitial;
        Champ.SelectAll();
        Champ.Focus();
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        Valider();
    }

    private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Champ_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Valider();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }

    private void Valider()
    {
        var nom = (Champ.Text ?? "").Trim();
        if (string.IsNullOrEmpty(nom))
        {
            Label.Text = "Nom vide, recommence :";
            Champ.Focus();
            Champ.SelectAll();
            return;
        }
        NomSaisi = nom;
        DialogResult = true;
        Close();
    }
}
