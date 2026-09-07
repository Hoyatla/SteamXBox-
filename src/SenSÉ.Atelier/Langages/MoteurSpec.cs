using System.Collections.Generic;
using System.IO;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Manifeste d'un moteur de langage (Outils/Langages/&lt;id&gt;/moteur.json).
/// </summary>
public sealed record MoteurSpec(
    string Id,
    string Nom,
    string Espace,
    string Rang,         // "embarque" | "demande" | "detecte"
    List<string> Extensions,
    string Detection,     // commande de detection (ex: "python --version")
    string Executable,    // chemin relatif au dossier du manifeste, OU nom dans PATH
    List<string> Commande, // ligne d execution : {executable} et {fichier} sont substitues
    bool Compile,
    List<string>? CommandeCompilation, // si compile=true : {executable} {fichier} {sortie}
    int LatenceMs,
    int TailleMo)
{
    /// <summary>Le dossier du manifeste (base des chemins relatifs).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Dossier { get; set; } = "";

    /// <summary>Chemin absolu resolu de l'executable.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ExecutableAbsolu =>
        Path.IsPathRooted(Executable) ? Executable : Path.GetFullPath(Path.Combine(Dossier, Executable));

    /// <summary>Commande resolue avec substitution des variables pour un fichier donne.</summary>
    public string[] ResoudreCommande(string fichier, string? sortie = null)
    {
        var args = new List<string>();
        foreach (var a in Commande)
        {
            args.Add(Substituer(a, fichier, sortie));
        }
        return args.ToArray();
    }

    /// <summary>Commande de compilation resolue.</summary>
    public string[] ResoudreCommandeCompilation(string fichier, string sortie)
    {
        if (CommandeCompilation is null) return new[] { ExecutableAbsolu, fichier };
        var args = new List<string>();
        foreach (var a in CommandeCompilation)
        {
            args.Add(Substituer(a, fichier, sortie));
        }
        return args.ToArray();
    }

    private string Substituer(string patron, string fichier, string? sortie)
    {
        var s = patron
            .Replace("{executable}", ExecutableAbsolu)
            .Replace("{fichier}", fichier);
        if (sortie is not null) s = s.Replace("{sortie}", sortie);
        return s;
    }
}