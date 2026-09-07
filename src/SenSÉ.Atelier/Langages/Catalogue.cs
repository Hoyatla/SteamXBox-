using System.Collections.Generic;
using System.Linq;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Référentiel de tous les langages connus du produit. Indépendant de la
/// machine : <see cref="Detecteur"/> dit ce qui est installé là, le Catalogue
/// dit ce qui pourrait être là. Sert à l'UI (liste à montrer) et à
/// <see cref="Installateur"/> (où télécharger, combien pèse, comment poser).
/// </summary>
/// <remarks>
/// Ce fichier n est pas un manifeste runtime : c est une table compilee dans
/// le binaire, comme les autres tables de reference du produit. Les détails
/// de téléchargement (URL, taille, méthode) sont decidés ici, pas dans
/// moteur.json qui ne décrit qu'un moteur deja sur la machine.
/// </remarks>
public static class Catalogue
{
    /// <summary>Type d'installation d'un langage.</summary>
    public enum TypeInstallation
    {
        /// <summary>Livré avec le produit (Outils/Python/, Outils/Langages/node/...).</summary>
        Embarque,
        /// <summary>Supposé présent sur la machine de l'utilisateur (PATH).</summary>
        Demande,
        /// <summary>Ni garanti ni livré : on regarde si ça existe.</summary>
        Detecte,
    }

    /// <summary>Entrée de catalogue : métadonnées d'installation d'un langage.</summary>
    public sealed record Entree(
        string Id,
        string Nom,
        TypeInstallation Type,
        string UrlTelechargement,
        long TailleMo,
        string CommandeInstallation,
        string Notes);

    /// <summary>Table de tous les langages connus. L'ordre est indicatif (alphabétique par id).</summary>
    public static IReadOnlyList<Entree> Entrees { get; } = new Entree[]
    {
        new("python", "Python 3.11", TypeInstallation.Embarque,
            "", 301,
            "deja_present",
            "Embarqué dans Outils/Python/. Toujours présent sur la machine."),

        new("node", "Node.js 24", TypeInstallation.Embarque,
            "", 101,
            "deja_present",
            "Copié dans Outils/Langages/node/ à l'installation. Toujours présent."),

        new("java", "Java 17 LTS (single-file source)", TypeInstallation.Demande,
            "https://download.oracle.com/java/17/latest/jdk-17_windows-x64_bin.zip", 60,
            "extraire_zip",
            "JDK 17 ou plus récent. single-file source-code (java fichier.java) évite l'étape javac."),

        new("c", "C (MinGW-W64 gcc 16)", TypeInstallation.Demande,
            "https://github.com/niXman/mingw-builds-binaries/releases", 914,
            "winget_brechtsanders",
            "winget install BrechtSanders.WinLibs.POSIX.UCRT. Fournit gcc + ld + les headers POSIX."),

        new("csharp", "C# 10+ (.NET SDK)", TypeInstallation.Demande,
            "https://dot.net/v1/dotnet-install.ps1", 2907,
            "dotnet_install_ps1",
            "SDK .NET 8 minimum (le SDK 10 tourne sans souci). dotnet run fichier.cs."),

        new("rust", "Rust 1.98 (rustc + cargo)", TypeInstallation.Detecte,
            "https://win.rustup.rs/x86_64", 4571,
            "rustup_init",
            "rustup-init.exe -y. Produit rustc + cargo. Pas garanti présent : on regarde."),
    };

    /// <summary>Cherche une entrée par id (insensible à la casse). Null si inconnu.</summary>
    public static Entree? Trouver(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return Entrees.FirstOrDefault(e => e.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Indique si un langage est installé sur la machine (Detecteur).</summary>
    public static bool EstInstalle(string id) => Detecteur.Trouver(id) is not null;

    /// <summary>Liste des langages absents de cette machine, mais présents au catalogue.</summary>
    public static IEnumerable<Entree> Manquants()
        => Entrees.Where(e => !EstInstalle(e.Id));
}