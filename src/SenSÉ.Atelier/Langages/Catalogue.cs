using System.Collections.Generic;
using System.Linq;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Référentiel de tous les langages connus du produit. Indépendant de la
/// machine : <see cref="Detecteur"/> dit ce qui est installé là, le Catalogue
/// dit ce qui pourrait étre là. Sert à l'UI (liste à montrer).
/// </summary>
/// <remarks>
/// Depuis le commit "tout embarque", tous les langages (sauf rust, shell,
/// swift) sont en TypeInstallation.Embarque : le produit les livre dans
/// Outils/Langages/<id>/, et la détection vérifie que le binaire est là.
/// rust reste en Detecte parce qu'il s'installe via rustup-init (hors
/// produit). shell et swift restent en Detecte parce qu'ils sont soit
/// livrés par Windows (powershell) soit rarement présents (swift).
/// </remarks>
public static class Catalogue
{
    /// <summary>Type d'installation d'un langage.</summary>
    public enum TypeInstallation
    {
        /// <summary>Livré avec le produit dans Outils/Langages/<id>/.</summary>
        Embarque,
        /// <summary>Plus utilisé : tous les langages courants sont embarqués. Conservé pour compatibilité.</summary>
        Demande,
        /// <summary>Ni garanti ni livré : on regarde si ça existe (rust, shell, swift).</summary>
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

    /// <summary>Table de tous les langages connus.</summary>
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

        new("java", "Java 17 LTS (jlink image)", TypeInstallation.Embarque,
            "", 25,
            "deja_present",
            "Image jlink minimale dans Outils/Langages/java/ (java.base + java.logging)."),

        new("c", "C (MinGW-W64 gcc 14)", TypeInstallation.Embarque,
            "", 664,
            "deja_present",
            "Binaire MinGW partagé avec cpp dans Outils/Langages/mingw/mingw64/."),

        new("csharp", "C# 10+ (.NET 10 SDK)", TypeInstallation.Embarque,
            "", 770,
            "deja_present",
            "SDK .NET 10 installé via dotnet-install.ps1 dans Outils/Langages/dotnet/."),

        new("rust", "Rust 1.98 (rustc + cargo)", TypeInstallation.Detecte,
            "https://win.rustup.rs/x86_64", 4571,
            "rustup_init",
            "Installé via rustup-init.exe -y. Pas garanti présent : on regarde."),

        new("cpp", "C++ (g++ via MinGW-W64)", TypeInstallation.Embarque,
            "", 0,
            "deja_present",
            "Mémes binaires que c (gcc + g++). Pas de coût additionnel."),

        new("go", "Go 1.24 SDK", TypeInstallation.Embarque,
            "", 194,
            "deja_present",
            "SDK Go dans Outils/Langages/go/go/ (zip archive)."),

        new("kotlin", "Kotlin 2.0.21 (scripts .kts)", TypeInstallation.Embarque,
            "", 90,
            "deja_present",
            "Dépend de java. kotlinc/bin/kotlinc.bat est un .bat, lancement via cmd /c. .kts uniquement pour MVP."),

        new("gradle", "Gradle 8.10.2 (build tool)", TypeInstallation.Embarque,
            "", 145,
            "deja_present",
            "Dépend de java. gradle-8.10.2/bin/gradle.bat est un .bat, lancement via cmd /c. Le code = la tàche à exécuter (build, test, run)."),

        new("shell", "PowerShell 5.1 (intégré Windows)", TypeInstallation.Detecte,
            "", 0,
            "deja_present",
            "Livré avec Windows. powershell.exe dans C:\\Windows\\System32. Toujours présent, jamais installé."),

        new("swift", "Swift (rare sur Windows)", TypeInstallation.Detecte,
            "https://www.swift.org/install/windows/", 0,
            "swift_installer",
            "Swift for Windows : installateur .exe. Rarement installé. On regarde, c est tout."),
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
