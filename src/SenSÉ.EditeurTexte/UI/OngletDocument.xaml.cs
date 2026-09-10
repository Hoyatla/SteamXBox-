using System;
using System.Windows.Controls;
using SenSÉ.EditeurTexte.Edition;

namespace SenSÉ.EditeurTexte.UI;

/// <summary>
/// Un onglet du TabControl. Encapsule un RichTextBox, son etat (chemin, titre, modifie),
/// et son instance d'Edition.Historique pour l'undo/redo. Les evenements EstModifie
/// et SelectionChangee permettent a MainWindow de mettre a jour toolbar/status bar.
/// </summary>
public partial class OngletDocument : UserControl
{
    public string? Chemin { get; set; }

    public string Titre =>
        Chemin is null ? "Sans titre" : System.IO.Path.GetFileName(Chemin);

    public bool EstSauvegarde { get; set; } = true;

    /// <summary>Historique d'undo/redo PER-ONGLET (Phase I.1).</summary>
    public Historique Historique { get; } = new();

    /// <summary>Acces au RichTextBox interne. Le champ est genere par InitializeComponent.</summary>
    public RichTextBox RtbInterne => Rtb;

    public event EventHandler? EstModifie;
    public event EventHandler? SelectionChangee;

    public OngletDocument()
    {
        InitializeComponent();
        Rtb.TextChanged += (_, _) =>
        {
            EstSauvegarde = false;
            EstModifie?.Invoke(this, EventArgs.Empty);
        };
        Rtb.SelectionChanged += (_, _) => SelectionChangee?.Invoke(this, EventArgs.Empty);
    }

    public void MarquerSauvegarde() => EstSauvegarde = true;
}
