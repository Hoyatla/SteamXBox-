using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SenSÉ.EditeurTexte.Persistance;

namespace SenSÉ.EditeurTexte.UI;

public partial class FenetreHistorique : Window
{
    private readonly string _cheminActuel;

    public FenetreHistorique(string cheminActuel)
    {
        InitializeComponent();
        _cheminActuel = cheminActuel;

        var versions = Historique.Lister(cheminActuel);
        LabelInfo.Text = versions.Count == 0
            ? "Aucune version archivee pour " + Path.GetFileName(cheminActuel) + "."
            : versions.Count + " version(s) archivee(s) pour " + Path.GetFileName(cheminActuel) + " (max " + Historique.MaxVersions + ").";
        ListeVersions.ItemsSource = versions;
    }

    private void ListeVersions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BtnRestaurer.IsEnabled = ListeVersions.SelectedItem is VersionHistorique;
    }

    private void BtnRestaurer_Click(object sender, RoutedEventArgs e)
    {
        if (ListeVersions.SelectedItem is not VersionHistorique v) return;
        var confirm = MessageBox.Show(this,
            "Restaurer la version " + v.Nom + " sur le fichier actuel ?" + Environment.NewLine + "Le contenu en cours sera ecrase.",
            "Restaurer une version",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            if (File.Exists(_cheminActuel))
            {
                Historique.Archiver(_cheminActuel);
            }
            File.Copy(v.Chemin, _cheminActuel, overwrite: true);
            AutoSave.Supprimer(_cheminActuel);
            MessageBox.Show(this, "Version restauree. Rechargez le fichier (Ctrl+O) pour la voir.",
                "Restauration", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Erreur de restauration",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnFermer_Click(object sender, RoutedEventArgs e) => Close();
}
