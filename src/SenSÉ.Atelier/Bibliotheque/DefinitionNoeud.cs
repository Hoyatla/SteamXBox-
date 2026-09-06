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
public sealed record DefinitionNoeud(
    string Id,
    string Nom,
    string Description,
    Espace Espace,
    string Categorie,
    IReadOnlyList<Port> PortsEntree,
    IReadOnlyList<Port> PortsSortie,
    IReadOnlyList<ParametreNoeud> Params,
    Func<ContexteExecution, ResultatExecution> Executeur
);

/// <summary>
/// Description d'un champ de parametre editable dans l'inspecteur.
/// </summary>
public sealed record ParametreNoeud(
    string Nom,
    string Libelle,
    string Type, // "texte", "multiligne", "nombre", "booleen", "chemin", "liste"
    object? Defaut = null,
    IReadOnlyList<string>? Valeurs = null,
    string? Indice = null,
    double? Min = null,
    double? Max = null
);