using System.IO;

namespace SenSÉ.Desktop.Debug;

/// <summary>
/// Purge les fichiers de diagnostic (.osk, .signal, .debug) vieux de plus
/// d'un mois, en FIFO. Les fichiers sont ranges par extension dans
/// <c>Debug\osk\</c>, <c>Debug\signal\</c>, <c>Debug\debug\</c> (sous-dossiers de la racine produit).
/// </summary>
/// <remarks>
/// <b>Le FIFO, c'est l'ordre alphabetique.</b> Le suffixe date
/// <c>yyyyMMdd-HHmmss</c> trie naturellement : le plus vieux est efface en
/// premier. Sans cette convention, deux fichiers de la meme seconde
/// pourraient partir dans n'importe quel ordre.
///
/// <para><b>Les ecrivains deposent maintenant dans le bon sous-dossier.</b>
/// Le helper <see cref="CheminDebug"/> fait respecter la convention
/// <c>Debug/{osk,signal,debug}/</c> des l'ecriture. Ce service ne fait
/// que purger ce que les ecrivains ont pose.</para>
/// </remarks>
public static class DebugFifo
{
    private const string Racine = "Debug";
    private const string ExtensionOsk = ".osk";
    private const string ExtensionSignal = ".signal";
    private const string ExtensionDebug = ".debug";
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    /// <summary>
    /// Assure que les trois sous-dossiers existent et purge ce qui est
    /// plus vieux que la retention. Idempotent et rapide.
    /// </summary>
    public static int Purger(Action<string>? journal = null)
    {
        var racine = Path.Combine(AppContext.BaseDirectory, Racine);
        Directory.CreateDirectory(racine);

        var effaces = PurgerSousDossier(Path.Combine(racine, "osk"), ExtensionOsk, journal);
        effaces += PurgerSousDossier(Path.Combine(racine, "signal"), ExtensionSignal, journal);
        effaces += PurgerSousDossier(Path.Combine(racine, "debug"), ExtensionDebug, journal);

        if (effaces > 0)
        {
            journal?.Invoke($"DebugFifo: {effaces} fichier(s) de debug purge(s) (> 30 jours)");
        }
        return effaces;
    }

    private static int PurgerSousDossier(string dossier, string extensionAttendue, Action<string>? journal)
    {
        Directory.CreateDirectory(dossier);
        var limite = DateTime.Now - Retention;
        var effaces = 0;

        var fichiers = Directory.GetFiles(dossier, "*" + extensionAttendue)
            .OrderBy(f => File.GetLastWriteTime(f))
            .ToList();

        foreach (var fichier in fichiers)
        {
            try
            {
                if (File.GetLastWriteTime(fichier) < limite)
                {
                    File.Delete(fichier);
                    effaces++;
                    journal?.Invoke(
                        $"DebugFifo: purge {Path.GetFileName(fichier)} ({extensionAttendue})");
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
                journal?.Invoke($"DebugFifo: pas les droits sur {Path.GetFileName(fichier)}");
            }
        }

        return effaces;
    }
}