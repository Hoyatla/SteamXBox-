using System;
using System.Collections.Generic;
using System.Linq;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque;

/// <summary>
/// Registre global des types de noeuds. Chaque noeud s'enregistre au
/// demarrage. Le serveur HTTP et l'UI lisent ce registre.
/// </summary>
public static class CatalogueNoeuds
{
    private static readonly Dictionary<string, DefinitionNoeud> _defs = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialise;

    public static void Enregistrer(DefinitionNoeud def)
    {
        _defs[def.Id] = def;
    }

    public static void Enregistrer(params DefinitionNoeud[] defs)
    {
        foreach (var d in defs) Enregistrer(d);
    }

    public static DefinitionNoeud? Trouver(string id)
        => _defs.TryGetValue(id, out var d) ? d : null;

    public static IReadOnlyList<DefinitionNoeud> Tous => _defs.Values.ToList();

    public static IReadOnlyList<DefinitionNoeud> ParEspace(Espace e)
        => _defs.Values.Where(d => d.Espace == e).ToList();

    public static IReadOnlyList<DefinitionNoeud> ParCategorie(string categorie)
        => _defs.Values.Where(d => string.Equals(d.Categorie, categorie, StringComparison.OrdinalIgnoreCase)).ToList();

    public static IEnumerable<string> Categories(Espace e)
        => _defs.Values.Where(d => d.Espace == e).Select(d => d.Categorie).Distinct();

    /// <summary>
    /// Initialise le registre avec tous les noeuds integres. Appele
    /// une seule fois au demarrage de l'Atelier.
    /// </summary>
    public static void InitialiserSiNecessaire()
    {
        if (_initialise) return;
        _initialise = true;
        Bibliotheque.Codage.Primitives.Enregistrer();
        Bibliotheque.Codage.Llm.Enregistrer();
        Bibliotheque.Codage.Mcp.Enregistrer();
        Bibliotheque.Codage.Math.Enregistrer();
        Bibliotheque.Codage.TexteAlgo.Enregistrer();
        Bibliotheque.Codage.Dates.Enregistrer();
        Bibliotheque.Codage.Listes.Enregistrer();
        Bibliotheque.Codage.Logique.Enregistrer();
        Bibliotheque.Codage.Donnees.Enregistrer();
        Bibliotheque.Codage.Reseau.Enregistrer();
        Bibliotheque.Codage.Variables.Enregistrer();
        Bibliotheque.Multimedia.Tous.Enregistrer();
        Composite.Tous.Enregistrer();
    }
}