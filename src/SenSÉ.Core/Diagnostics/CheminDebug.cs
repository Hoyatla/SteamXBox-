using System;
using System.IO;

namespace SenSÉ.Core.Diagnostics;

/// <summary>
/// Resout les chemins des fichiers de diagnostic (.signal, .log OSK, .log debug).
/// Convention : tous les fichiers partent de `<BaseDirectory>/Debug/<categorie>/<fichier>`,
/// categorie in {signal, osk, debug}. Le dossier racine est cree paresseusement a la
/// premiere utilisation via <see cref="AssurerRacine"/>.
/// </summary>
/// <remarks>
/// Avant ce helper, les ecrivains utilisaient tous <c>Path.Combine(AppContext.BaseDirectory, nom)</c>
/// et deposaient les fichiers a la racine du produit, melanges avec les binaires. La convention
/// <c>Debug/{signal,osk,debug}/</c> est documentee depuis longtemps (cf. DebugFifo.cs) mais
/// n'etait pas appliquee par les ecrivains - uniquement par FIFO cleanup. Ce helper la fait
/// respecter des l'ecriture.
/// </remarks>
public static class CheminDebug
{
    public const string SousDossierSignal = "signal";
    public const string SousDossierOsk = "osk";
    public const string SousDossierDebug = "debug";

    public static string Racine { get; } = Path.Combine(AppContext.BaseDirectory, "Debug");

    public static string Signal(string nom) =>
        Path.Combine(Racine, SousDossierSignal, nom + ".signal");

    public static string OskLog(string stem, string? suffixe = null) =>
        Path.Combine(Racine, SousDossierOsk,
            "SenSÉ-" + stem + (string.IsNullOrEmpty(suffixe) ? "" : "-" + suffixe) + "-debug.log");

    public static string DebugLog(string process) =>
        Path.Combine(Racine, SousDossierDebug, "SenSÉ-" + process + "-debug.log");

    /// <summary>Cree les 3 sous-dossiers si manquants. Idempotent.</summary>
    public static void AssurerRacine()
    {
        Directory.CreateDirectory(Path.Combine(Racine, SousDossierSignal));
        Directory.CreateDirectory(Path.Combine(Racine, SousDossierOsk));
        Directory.CreateDirectory(Path.Combine(Racine, SousDossierDebug));
    }
}
