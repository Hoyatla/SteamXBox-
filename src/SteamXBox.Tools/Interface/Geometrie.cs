using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamXBox.Tools.Interface;

/// <summary>Où une fenêtre était, et comment elle était.</summary>
/// <param name="Gauche">Bord gauche, en pixels indépendants du périphérique.</param>
/// <param name="Haut">Bord supérieur.</param>
/// <param name="Largeur">Largeur, jamais nulle.</param>
/// <param name="Hauteur">Hauteur, jamais nulle.</param>
/// <param name="Maximisee">Vrai si elle était agrandie ; les quatre autres valeurs sont alors
/// celles qu'elle retrouvera en étant restaurée.</param>
public sealed record Cadre(
    [property: JsonPropertyName("gauche")] double Gauche,
    [property: JsonPropertyName("haut")] double Haut,
    [property: JsonPropertyName("largeur")] double Largeur,
    [property: JsonPropertyName("hauteur")] double Hauteur,
    [property: JsonPropertyName("maximisee")] bool Maximisee);

/// <summary>
/// Un seul endroit où toutes les fenêtres du produit retiennent leur taille et leur place.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci corrige.</b> Aucune fenêtre ne retenait rien. Chaque ouverture repartait
/// des dimensions écrites dans le XAML, centrée sur l'écran — l'assistant à 560×640, l'atelier à
/// 640×700 — et l'utilisateur qui les avait agrandies recommençait à chaque session.
///
/// <para>
/// <b>Un magasin unique plutôt qu'une logique par fenêtre.</b> Six fenêtres et autant de panneaux
/// d'outils, chacun avec son bout de code de sauvegarde, feraient six occasions d'oublier un cas —
/// la fenêtre agrandie, l'écran débranché, le fichier illisible. Une clé par fenêtre suffit à les
/// distinguer, et les panneaux d'outils prennent la leur de l'identifiant de l'outil : chacun garde
/// donc sa propre taille sans que le mécanisme sache ce qu'est un outil.
/// </para>
///
/// <para>
/// <b>Ce qui est écrit est ce qu'on retrouve, pas ce qu'on voit.</b> Une fenêtre agrandie rend des
/// dimensions d'écran entier ; les enregistrer telles quelles la ferait revenir agrandie sans qu'on
/// puisse la réduire à ce qu'elle était. Le cadre porte donc les dimensions restaurées, et l'état
/// agrandi à côté.
/// </para>
/// </remarks>
public static class Geometrie
{
    /// <summary>Le dossier parent, ou null pour celui du produit. Sert aux épreuves.</summary>
    /// <remarks>
    /// Même seam que <c>FichierTravail.Racine</c>, et pour la même raison : sans elle, une épreuve
    /// écrit dans le fichier de la personne qui la lance et lui déplace ses fenêtres.
    /// </remarks>
    public static string? Racine { get; set; }

    private static string Dossier
        => Racine ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SteamXBox");

    private static string Fichier => Path.Combine(Dossier, "fenetres.json");

    /// <summary>Un seul écrivain à la fois dans ce processus.</summary>
    /// <remarks>
    /// Deux fenêtres qui se ferment ensemble — ce qui arrive à chaque sortie du produit — lisaient
    /// toutes deux l'état d'avant, et la seconde effaçait la place de la première.
    /// </remarks>
    private static readonly object Verrou = new();

    /// <summary>Où cette fenêtre était la dernière fois, ou null si on ne l'a jamais vue.</summary>
    public static Cadre? Lire(string cle, Action<string>? journal = null)
    {
        if (cle.Length == 0)
        {
            return null;
        }

        lock (Verrou)
        {
            return Tout(journal).GetValueOrDefault(cle);
        }
    }

    /// <summary>Retient où cette fenêtre est.</summary>
    /// <remarks>
    /// Un cadre sans surface n'est pas retenu : une fenêtre réduite dans la barre des tâches rend
    /// des dimensions nulles ou négatives, et les écrire condamnerait la fenêtre à rouvrir
    /// invisible — un défaut dont l'utilisateur ne peut pas sortir, puisqu'il faudrait voir la
    /// fenêtre pour la redimensionner.
    /// </remarks>
    public static void Ecrire(string cle, Cadre cadre, Action<string>? journal = null)
    {
        if (cle.Length == 0 || cadre.Largeur <= 0 || cadre.Hauteur <= 0)
        {
            return;
        }

        lock (Verrou)
        {
            try
            {
                Directory.CreateDirectory(Dossier);

                var tout = Tout(journal);
                tout[cle] = cadre;

                File.WriteAllText(Fichier, JsonSerializer.Serialize(tout, Ecriture));
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Une place non retenue est un désagrément ; une fenêtre qui refuse de se fermer
                // parce qu'elle n'a pas pu écrire serait une panne.
                journal?.Invoke($"géométrie non retenue pour « {cle} » : {exception.Message}");
            }
        }
    }

    private static readonly JsonSerializerOptions Ecriture = new() { WriteIndented = true };

    private static Dictionary<string, Cadre> Tout(Action<string>? journal)
    {
        try
        {
            if (!File.Exists(Fichier))
            {
                return new Dictionary<string, Cadre>(StringComparer.OrdinalIgnoreCase);
            }

            var lu = JsonSerializer.Deserialize<Dictionary<string, Cadre>>(File.ReadAllText(Fichier));

            return lu is null
                ? new Dictionary<string, Cadre>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Cadre>(lu, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            journal?.Invoke($"géométries illisibles, on repart des tailles d'origine : {exception.Message}");

            return new Dictionary<string, Cadre>(StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>La clé d'un panneau d'outil : la sienne, pour qu'il garde sa propre taille.</summary>
    public static string Outil(string identifiant) => "outil:" + identifiant;

    /// <summary>
    /// Ce cadre est-il encore visible sur ce bureau ?
    /// </summary>
    /// <remarks>
    /// <b>Un écran débranché ne doit pas emporter la fenêtre avec lui.</b> Restaurer une place
    /// enregistrée sur un second moniteur qui n'est plus là ouvre la fenêtre hors du bureau : elle
    /// existe, elle a le focus, et l'utilisateur ne la voit pas — il croit le produit planté.
    ///
    /// <para>
    /// Le critère est le recouvrement, pas l'inclusion : une fenêtre volontairement à cheval sur un
    /// bord reste légitime, et exiger qu'elle tienne entière la recentrerait sans raison. Cent
    /// pixels de barre de titre atteignables suffisent à la reprendre à la souris.
    /// </para>
    /// </remarks>
    public static bool Visible(Cadre cadre, double bureauGauche, double bureauHaut, double bureauLargeur, double bureauHauteur)
    {
        const double Prise = 100;

        var droite = Math.Min(cadre.Gauche + cadre.Largeur, bureauGauche + bureauLargeur);
        var gauche = Math.Max(cadre.Gauche, bureauGauche);
        var bas = Math.Min(cadre.Haut + cadre.Hauteur, bureauHaut + bureauHauteur);
        var haut = Math.Max(cadre.Haut, bureauHaut);

        return droite - gauche >= Prise && bas - haut >= Prise;
    }
}
