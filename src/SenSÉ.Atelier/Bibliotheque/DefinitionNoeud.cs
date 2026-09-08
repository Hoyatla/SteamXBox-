using System;
using System.Collections.Generic;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque;

/// <summary>
/// Description d'un type de noeud. Le moteur l'utilise pour savoir
/// quels ports existent et comment executer le noeud.
/// </summary>
/// <param name="Id">Identifiant unique (ex: "texte_vers_image").</param>
/// <param name="Nom">Nom affiche dans la palette (ex: "Texte vers Image").</param>
/// <param name="Description">Une phrase + cas d'usage.</param>
/// <param name="Espace">Codage ou Multimedia.</param>
/// <param name="Categorie">Titre de la categorie palette (Texte, Code, MCPs, Image...).</param>
/// <param name="PortsEntree">Ports d'entree declares.</param>
/// <param name="PortsSortie">Ports de sortie declares.</param>
/// <param name="Params">Champs editables dans l'inspecteur.</param>
/// <param name="Executeur">Fonction appelee au runtime.</param>
/// <param name="ModeleId">Id du modele (dans Outils/Modeles/{image,video}/) que ce noeud
/// fait tourner. Vide/null = pas de GPU. Plusieurs noeuds du meme graphe peuvent
/// partager le meme id : le moteur les traite ensemble, un seul chargement.
/// <b>Positionne en dernier avec defaut null</b> pour ne pas casser les call sites
/// existants (qui passent 9 arguments et ignorent le modele).</param>
/// <param name="NomVulgarise">Label principal affiche a l'utilisateur dans la palette
/// et sur le noeud. Vide/null = fallback sur <c>Nom</c>.</param>
/// <param name="DescriptionLongue">Explication longue, 1-2 phrases, affichee en tooltip
/// au survol. Vide/null = fallback sur <c>Description</c>.</param>
public sealed record DefinitionNoeud(
    string Id,
    string Nom,
    string Description,
    Espace Espace,
    string Categorie,
    IReadOnlyList<Port> PortsEntree,
    IReadOnlyList<Port> PortsSortie,
    IReadOnlyList<ParametreNoeud> Params,
    Func<ContexteExecution, Task<ResultatExecution>> Executeur,
    string? ModeleId = null,
    string? NomVulgarise = null,
    string? DescriptionLongue = null
)
{
    /// <summary>Label principal affiche a l'utilisateur. Chaine de fallback :
    /// NomVulgarise (champs explicite) -> Vulgarisation.LookupNoeud(Id) (mapping
    /// centralise) -> Nom (defaut).</summary>
    public string NomAffichage
    {
        get
        {
            if (!string.IsNullOrEmpty(NomVulgarise)) return NomVulgarise;
            var v = Vulgarisation.LookupNoeud(Id);
            if (v.HasValue) return v.Value.NomVulgarise;
            return Nom;
        }
    }
    /// <summary>Explication longue. Chaine de fallback : DescriptionLongue (champ
    /// explicite) -> Vulgarisation.LookupNoeud(Id) -> Description (defaut).</summary>
    public string DescriptionAffichage
    {
        get
        {
            if (!string.IsNullOrEmpty(DescriptionLongue)) return DescriptionLongue;
            var v = Vulgarisation.LookupNoeud(Id);
            if (v.HasValue) return v.Value.DescriptionLongue;
            return Description;
        }
    }
};

/// <summary>
/// Description d'un champ de parametre editable dans l'inspecteur.
/// </summary>
/// <param name="NomVulgarise">Label utilisateur affiche dans l'inspecteur.
/// Vide/null = fallback sur <c>Nom</c> (en remplacant les underscores par des espaces).</param>
/// <param name="DescriptionLongue">Explication longue affichee en tooltip. Vide/null = fallback sur <c>Description</c>.</param>
public sealed record ParametreNoeud(
    string Nom,
    string Libelle,
    string Type, // "texte", "multiligne", "nombre", "booleen", "chemin", "liste"
    object? Defaut = null,
    IReadOnlyList<string>? Valeurs = null,
    string? Indice = null,
    double? Min = null,
    double? Max = null,
    string? NomVulgarise = null,
    string? DescriptionLongue = null
)
{
    /// <summary>Label utilisateur affiche dans l'inspecteur. Chaine de fallback :
    /// NomVulgarise (explicite) -> Vulgarisation.LookupParametre(Nom) (mapping
    /// centralise) -> Libelle (defaut technique) -> Nom.Replace('_',' ').</summary>
    public string LibelleAffichage
    {
        get
        {
            if (!string.IsNullOrEmpty(NomVulgarise)) return NomVulgarise;
            var v = Vulgarisation.LookupParametre(Nom);
            if (v.HasValue) return v.Value.NomVulgarise;
            if (!string.IsNullOrEmpty(Libelle)) return Libelle;
            return Nom.Replace('_', ' ');
        }
    }
    /// <summary>Explication longue. Chaine de fallback : DescriptionLongue ->
    /// Vulgarisation.LookupParametre(Nom) -> Description (defaut).</summary>
    public string DescriptionAffichage
    {
        get
        {
            if (!string.IsNullOrEmpty(DescriptionLongue)) return DescriptionLongue;
            var v = Vulgarisation.LookupParametre(Nom);
            if (v.HasValue) return v.Value.DescriptionLongue;
            return Libelle;
        }
    }
};