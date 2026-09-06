using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using SenSÉ.Atelier.Bibliotheque;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.UI;

public partial class Inspecteur : UserControl
{
    public event Action<Noeud, Dictionary<string, object?>>? Modifie;

    public Inspecteur()
    {
        InitializeComponent();
        Vider();
    }

    public void Vider()
    {
        Titre.Text = "— Aucun noeud —";
        Description.Text = "Cliquez sur un noeud pour voir ses paramètres.";
        Form.Children.Clear();
    }

    public void Afficher(Noeud n)
    {
        var def = CatalogueNoeuds.Trouver(n.Type);
        Titre.Text = def?.Nom ?? n.Type;
        Description.Text = def?.Description ?? "";
        Form.Children.Clear();
        if (def is null) return;
        foreach (var p in def.Params)
        {
            Form.Children.Add(new TextBlock
            {
                Text = p.Libelle,
                Foreground = (System.Windows.Media.Brush)TryFindResource("TexteSecondaireBrush") ?? System.Windows.Media.Brushes.Gray,
                FontSize = 11, Margin = new Thickness(0, 8, 0, 2),
            });
            FrameworkElement champ = p.Type switch
            {
                "multiligne" => new TextBox
                {
                    Text = n.Params.TryGetValue(p.Nom, out var v) ? v?.ToString() ?? "" : p.Defaut?.ToString() ?? "",
                    AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 60, Tag = p.Nom,
                },
                "nombre" => new TextBox
                {
                    Text = n.Params.TryGetValue(p.Nom, out var v) ? v?.ToString() ?? "" : p.Defaut?.ToString() ?? "0",
                    Tag = p.Nom,
                },
                "booleen" => new CheckBox
                {
                    IsChecked = n.Params.TryGetValue(p.Nom, out var v) && v is bool b ? b
                              : p.Defaut is bool db && db,
                    Content = "activé", Tag = p.Nom,
                },
                "liste" => CreerComboListe(p, n),
                _ => new TextBox
                {
                    Text = n.Params.TryGetValue(p.Nom, out var v) ? v?.ToString() ?? "" : p.Defaut?.ToString() ?? "",
                    Tag = p.Nom,
                },
            };
            Form.Children.Add(champ);
            if (champ is TextBox tb) tb.TextChanged += (s, e) => Notifier(n, p.Nom, tb.Text);
            if (champ is CheckBox cb) cb.Checked += (s, e) => Notifier(n, p.Nom, true);
            if (champ is CheckBox cb2) cb2.Unchecked += (s, e) => Notifier(n, p.Nom, false);
            if (champ is ComboBox cmb) cmb.SelectionChanged += (s, e) => Notifier(n, p.Nom, cmb.SelectedItem?.ToString() ?? "");
        }
    }

    private ComboBox CreerComboListe(ParametreNoeud p, Noeud n)
    {
        var cmb = new ComboBox { Tag = p.Nom };
        if (p.Valeurs is not null)
        {
            foreach (var v in p.Valeurs) cmb.Items.Add(v);
        }
        var cur = n.Params.TryGetValue(p.Nom, out var v2) ? v2?.ToString()
                : p.Defaut?.ToString();
        if (cur is not null) cmb.SelectedItem = cur;
        return cmb;
    }

    private void Notifier(Noeud n, string cle, object? val)
    {
        n.Params[cle] = val;
        var copie = new Dictionary<string, object?>(n.Params);
        Modifie?.Invoke(n, copie);
    }
}