using System.Runtime.CompilerServices;
using SenSÉ.Plugins;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Deplace la storage de l'hote dans un dossier temporaire, pour toute la serie.
/// </summary>
/// <remarks>
/// Les tests des outils enregistrent de vraies decisions dans le vrai fichier de reglages. Avant
/// ceci, lancer la serie laissait onze identifiants <c>test-SenSÉ-…</c> et un <c>essai</c> dans
/// <c>%LOCALAPPDATA%\SenSÉ\plugins-choices.json</c> — l'installation de la personne qui lance
/// les tests, modifiee par les tests. Ce n'est pas un detail d'isolation : c'est la serie qui abime
/// ce qu'elle est censee verifier.
///
/// <para>
/// Un initialiseur de module plutot qu'un fixture : il s'execute avant tout le reste de cet
/// assemblage, donc avant qu'un test ait pu lire quoi que ce soit. Un fixture de collection
/// n'aurait couvert que sa collection, et la variable doit etre posee une fois pour le processus.
/// </para>
/// </remarks>
internal static class TestHostStorage
{
    /// <summary>Un dossier stable, vide au demarrage de chaque serie.</summary>
    /// <remarks>
    /// Stable et non unique par execution : un nom tire au sort laisserait un dossier de plus dans
    /// le temporaire a chaque lancement. Vide au demarrage, parce qu'un test qui suppose "cet outil
    /// n'a jamais ete touche" doit pouvoir le supposer.
    ///
    /// <para>
    /// Les fichiers sont effaces un a un, et non le dossier lui-meme. Windows ne retire pas un
    /// dossier au moment ou on le supprime : il le marque, et le retire quand le dernier
    /// descripteur se ferme. Le recreer aussitot peut donc echouer, ou reussir sur un dossier
    /// encore en train de disparaitre — de quoi faire echouer des ecritures pendant les premieres
    /// secondes de la serie, ce qui est exactement le genre d'instabilite qu'on est en train de
    /// retirer d'ici.
    /// </para>
    /// </remarks>
    [ModuleInitializer]
    internal static void RedirectAwayFromTheRealInstallation()
    {
        var root = Path.Combine(Path.GetTempPath(), "SenSÉ-tests-storage");

        try
        {
            Directory.CreateDirectory(root);

            foreach (var leftover in Directory.EnumerateFiles(root))
            {
                try
                {
                    File.Delete(leftover);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Un reste d'une execution precedente est sans consequence : chaque test se
                    // donne un identifiant unique, donc rien de ce qui traine ne peut lui repondre.
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Un dossier qu'on ne peut ni vider ni creer reste preferable au vrai fichier de
            // reglages : la variable est posee quand meme.
        }

        Environment.SetEnvironmentVariable(PluginLifecycle.StorageRootVariable, root);
    }
}
