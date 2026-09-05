using System.Windows;
using SenSÉ.Tools.Interface;

namespace SenSÉ.Desktop;

/// <summary>
/// Branche une fenêtre sur le magasin de géométrie : elle rouvre où on l'avait laissée.
/// </summary>
/// <remarks>
/// <b>Une seule ligne par fenêtre, et rien d'autre à écrire.</b> C'est tout l'objet d'un mécanisme
/// unique : six fenêtres et autant de panneaux d'outils qui porteraient chacun leur sauvegarde
/// feraient six occasions d'oublier le même cas — l'agrandissement, l'écran débranché, la fenêtre
/// réduite. Ici le cas est traité une fois.
/// </remarks>
public static class SuiviFenetre
{
    /// <summary>Fait retenir à cette fenêtre sa taille et sa place, sous cette clé.</summary>
    /// <param name="fenetre">La fenêtre à suivre.</param>
    /// <param name="cle">
    /// Ce qui la distingue des autres. Pour un panneau d'outil, <see cref="Geometrie.Outil"/> :
    /// chaque outil garde alors sa propre taille.
    /// </param>
    /// <param name="journal">Reçoit ce qui n'a pas pu être retenu.</param>
    public static void Suivre(Window fenetre, string cle, Action<string>? journal = null)
    {
        // À SourceInitialized et non à Loaded : la fenêtre a son handle mais n'est pas encore
        // dessinée, donc la restauration ne se voit pas. À Loaded, l'utilisateur verrait la fenêtre
        // apparaître à sa taille du XAML puis sauter à la sienne.
        fenetre.SourceInitialized += (_, _) => Restaurer(fenetre, cle);

        // À Closing plutôt qu'à Closed : à Closed, Left et Top valent déjà NaN sur certaines
        // fermetures, et on retiendrait une place qui n'existe pas.
        fenetre.Closing += (_, _) => Retenir(fenetre, cle, journal);
    }

    private static void Restaurer(Window fenetre, string cle)
    {
        if (Geometrie.Lire(cle) is not { } cadre)
        {
            return;
        }

        if (!Geometrie.Visible(
                cadre,
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight))
        {
            // L'écran qui portait cette place n'est plus là. On laisse le XAML placer la fenêtre :
            // au centre, visible, plutôt qu'au bon endroit d'un bureau disparu.
            return;
        }

        // Une fenêtre qui se dimensionne sur son contenu ignore la hauteur qu'on lui donne. Dès
        // lors qu'on lui en rend une, c'est qu'elle a été redimensionnée à la main : la règle du
        // contenu a fait son travail à la première ouverture et cède la place au choix de
        // l'utilisateur.
        if (fenetre.SizeToContent != SizeToContent.Manual)
        {
            fenetre.SizeToContent = SizeToContent.Manual;
        }

        fenetre.WindowStartupLocation = WindowStartupLocation.Manual;
        fenetre.Left = cadre.Gauche;
        fenetre.Top = cadre.Haut;
        fenetre.Width = cadre.Largeur;
        fenetre.Height = cadre.Hauteur;

        if (cadre.Maximisee)
        {
            fenetre.WindowState = WindowState.Maximized;
        }
    }

    private static void Retenir(Window fenetre, string cle, Action<string>? journal)
    {
        // RestoreBounds quand elle est agrandie ou réduite : Left et Width valent alors les
        // dimensions de l'écran entier, ou n'importe quoi. Ce qu'on veut retenir est la taille
        // qu'elle retrouvera en étant restaurée, pas celle qu'elle occupe.
        var cadre = fenetre.WindowState == WindowState.Normal
            ? new Cadre(fenetre.Left, fenetre.Top, fenetre.ActualWidth, fenetre.ActualHeight, false)
            : new Cadre(
                fenetre.RestoreBounds.Left,
                fenetre.RestoreBounds.Top,
                fenetre.RestoreBounds.Width,
                fenetre.RestoreBounds.Height,
                fenetre.WindowState == WindowState.Maximized);

        if (double.IsNaN(cadre.Gauche) || double.IsNaN(cadre.Haut))
        {
            return;
        }

        Geometrie.Ecrire(cle, cadre, journal);
    }
}
