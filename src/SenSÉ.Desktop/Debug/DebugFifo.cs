using System.IO;

namespace SenSÉ.Desktop.Debug;

/// <summary>
/// Purge les fichiers de diagnostic (.osk, .signal, .debug) vieux de plus
/// d'un mois, en FIFO. Les fichiers sont ranges par extension dans
/// <c>Outils\Debug\osk\</c>, <c>Outils\Debug\signal\</c>, <c>Outils\Debug\debug\</c>.
/// </summary>
/// <remarks>
/// <b>Le FIFO, c'est l'ordre alphabetique.</b> Le suffixe date
/// <c>yyyyMMdd-HHmmss</c> trie naturellement : le plus vieux est efface en
/// premier. Sans cette convention, deux fichiers de la meme seconde
/// pourraient partir dans n'importe quel ordre.
///
/// <para><b>Pas de deplacement des fichiers existants.</b> Le bridge qui
/// cree ces fichiers les pose dans son propre dossier, et c'est une
/// autre conversation. Ce service ne fait que purger les sous-dossiers
/// cibles. Pour que les nouveaux fichiers soient ranges au bon endroit,
/// il faut que le bridge les y depose directement.</para>
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