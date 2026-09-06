using System;
using System.IO;
using System.Windows;
using SenSÉ.Atelier.Custom;

namespace SenSÉ.Atelier.UI;

public partial class FenetreNoeudCustom : Window
{
    public NoeudCustom? Resultat { get; private set; }

    public FenetreNoeudCustom()
    {
        InitializeComponent();
        // Code par defaut : exemple minimal qui retourne la longueur de l'entree
        TxtCode.Text =
            "# Lis l'entree 'valeur' et produit la sortie 'resultat'\n" +
            "resultat = (valeur or '').upper()\n";
    }

    private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void BtnCreer_Click(object sender, RoutedEventArgs e)
    {
        var nom = (TxtNom.Text ?? "").Trim();
        if (string.IsNullOrEmpty(nom))
        {
            MessageBox.Show("Le nom est obligatoire.", "Noeud custom", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var code = TxtCode.Text ?? "";
        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Le code est vide.", "Noeud custom", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var espace = (CmbEspace.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string ?? "codage";
        var langage = (CmbLangage.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string ?? "python";
        var categorie = string.IsNullOrWhiteSpace(TxtCategorie.Text) ? "Perso" : TxtCategorie.Text.Trim();

        var nc = new NoeudCustom
        {
            Id = Slug(nom),
            Nom = nom,
            Description = TxtDescription.Text ?? "",
            Espace = espace,
            Langage = langage,
            Categorie = categorie,
            Code = code,
        };
        // Ports par defaut : in:valeur (Texte), out:resultat (Texte)
        nc.PortsEntree.Add(new NoeudCustom.PortDto { Nom = "valeur", Type = "Texte" });
        nc.PortsSortie.Add(new NoeudCustom.PortDto { Nom = "resultat", Type = "Texte" });

        // Trouve la racine de persistance
        var racine = TrouverRacine();
        if (racine is null)
        {
            MessageBox.Show("Racine Atelier introuvable.", "Noeud custom", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            CatalogueCustom.Creer(nc, racine);
            Resultat = nc;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Echec creation : " + ex.Message, "Noeud custom", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? TrouverRacine()
    {
        // 1) Variable d'env
        var env = Environment.GetEnvironmentVariable("ATELIER_RACINE");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        // 2) %APPDATA%\SenSÉ\Atelier
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var p = Path.Combine(appData, "SenSÉ", "Atelier");
        if (Directory.Exists(p)) return p;
        // 3) Outils\Atelier a cote de l'exe
        var exe = System.AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(exe))
        {
            var dir = Path.GetDirectoryName(exe);
            if (dir is not null)
            {
                p = Path.Combine(dir, "Outils", "Atelier");
                if (Directory.Exists(p)) return p;
                // En debug : on est dans bin\Debug\net8.0-windows, on remonte
                p = Path.Combine(dir, "..", "..", "..", "Outils", "Atelier");
                if (Directory.Exists(Path.GetFullPath(p))) return Path.GetFullPath(p);
            }
        }
        return null;
    }

    private static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        var r = sb.ToString().Trim('_');
        return r.Length == 0 ? "noeud_" + Guid.NewGuid().ToString("N").Substring(0, 6) : r;
    }
}