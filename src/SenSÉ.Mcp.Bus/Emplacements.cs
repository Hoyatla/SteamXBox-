namespace SenSÉ.Mcp.Bus;

/// <summary>
/// Où sont les choses, vu depuis n'importe lequel des exécutables de SenSÉ.
/// </summary>
/// <remarks>
/// <b>Le défaut que ceci empêche.</b> Les serveurs vivent dans des sous-dossiers —
/// <c>Outils\Cdp\</c>, <c>Outils\McpSaisie\</c>, <c>Outils\DebugAgent\</c> — et pour eux
/// <see cref="AppContext.BaseDirectory"/> désigne ce sous-dossier, pas le produit. Composer
/// « BaseDirectory + Outils » y crée un second <c>Outils</c> imbriqué dans le premier.
///
/// <para>Ce n'est pas une crainte théorique : <c>Outils\Cdp\Outils\CdpUserData</c> et
/// <c>Outils\McpSaisie\Captures</c> existent tous deux sur cette machine, laissés par exactement
/// cette erreur, faite deux fois par deux serveurs différents. La troisième fois aurait rangé un
/// navigateur de 357 Mo au mauvais endroit.</para>
///
/// <para>D'où un seul endroit qui sait répondre, plutôt que la même ligne recopiée à chaque
/// nouveau besoin.</para>
/// </remarks>
public static class Emplacements
{
    /// <summary>
    /// La racine du produit, quel que soit l'exécutable qui pose la question.
    /// </summary>
    /// <remarks>
    /// On remonte jusqu'à l'exécutable qui ne peut être qu'à la racine. Le repli sur
    /// <see cref="AppContext.BaseDirectory"/> couvre le binaire lancé hors de son installation —
    /// un test, un dossier de publication — où il n'y a rien de mieux à dire.
    /// </remarks>
    public static string RacineProduit()
    {
        var dossier = new DirectoryInfo(AppContext.BaseDirectory);

        for (var remontees = 0; dossier is not null && remontees < 4; remontees++)
        {
            if (File.Exists(Path.Combine(dossier.FullName, "SenSÉ.Desktop.exe")))
            {
                return dossier.FullName;
            }

            dossier = dossier.Parent;
        }

        return AppContext.BaseDirectory;
    }

    /// <summary>Le dossier des captures d'écran, commun à tout le produit.</summary>
    /// <remarks>
    /// Un seul, et pas un par serveur. mcp-saisie écrivait dans <c>Outils\McpSaisie\Captures</c>
    /// pendant que pc-agent écrivait dans <c>Captures</c> à la racine : deux dossiers pour la même
    /// chose, dont un que l'utilisateur ne trouve pas et que rien ne purge.
    /// </remarks>
    public static string Captures() => Path.Combine(RacineProduit(), "Captures");
}
