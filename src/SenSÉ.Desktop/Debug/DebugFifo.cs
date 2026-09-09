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
    /// <summary>
    /// Assure que les trois sous-dossiers existent et purge ce qui est
    /// plus vieux que la retention. Idempotent et rapide.
    /// </summary>
    /// <remarks>
    /// <b>Deux dossiers sur trois n'etaient jamais purges.</b> Ce service filtrait par extension —
    /// <c>Debug/osk/*.osk</c> et <c>Debug/debug/*.debug</c> — alors que les ecrivains y deposent
    /// des <c>.log</c> : <c>SenSÉ-Xbox-790c71b1-debug.log</c>, <c>SenSÉ-desktop-debug.log</c>.
    /// Seul <c>signal/</c>, dont l'extension coincidait, etait tenu. Les deux autres grossissaient
    /// sans fin, et le menage annonce dans le journal portait sur du vide.
    ///
    /// <para>
    /// La regle est desormais l'age seul, tenue par <see cref="CheminDebug.Purger"/> a cote de
    /// ceux qui composent ces chemins — un filtre qui doit deviner comment un autre fichier a ete
    /// nomme se trompe des que ce nom change.
    /// </para>
    /// </remarks>
    public static int Purger(Action<string>? journal = null)
    {
        SenSÉ.Core.Diagnostics.CheminDebug.AssurerRacine();

        var effaces = SenSÉ.Core.Diagnostics.CheminDebug.Purger(
            SenSÉ.Core.Diagnostics.CheminDebug.Peremption);

        if (effaces > 0)
        {
            journal?.Invoke($"DebugFifo: {effaces} fichier(s) de debug purge(s) (> 30 jours)");
        }

        return effaces;
    }
}