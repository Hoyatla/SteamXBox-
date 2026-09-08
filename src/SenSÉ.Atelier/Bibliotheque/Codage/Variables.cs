using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// 3 noeuds de variables : variable_set, variable_get, variable_compteur.
/// Store en memoire, cle/value, scope = process (reset au reboot).
/// </summary>
public static class Variables
{
    private static readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();

    public static void Enregistrer()
    {
        // 23. variable_set
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "variable_set", "variable_set",
            "Stocke une valeur dans le store de variables (cle texte). Le store est partage par tout le processus, reset au reboot.",
            Espace.Codage, "Variables",
            new List<Port> { new("valeur", TypePort.Texte, true) },
            new List<Port>
            {
                new("cle", TypePort.Texte, false),
                new("ancienne", TypePort.Texte, false),
            },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", "ma_variable"),
            },
            ctx =>
            {
                var cle = ctx.Ch("cle", "ma_variable");
                var valeur = ctx.Entree("valeur") ?? "";
                if (string.IsNullOrEmpty(cle))
                    return Task.FromResult(ResultatExecution.Fail("variable_set: cle vide"));
                string? ancienne;
                lock (_lock)
                {
                    _store.TryGetValue(cle, out ancienne);
                    _store[cle] = valeur;
                }
                return Task.FromResult(ResultatExecution.Ok(new()
                {
                    ["cle"] = cle,
                    ["ancienne"] = ancienne ?? "",
                }));
            }
        ));

        // 24. variable_get
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "variable_get", "variable_get",
            "Recupere la valeur d'une variable du store. Retourne la valeur par defaut si la cle n'existe pas.",
            Espace.Codage, "Variables",
            new List<Port>(),
            new List<Port>
            {
                new("valeur", TypePort.Texte, false),
                new("existe", TypePort.Booleen, false),
            },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", "ma_variable"),
                new("defaut", "Defaut si absent", "texte", ""),
            },
            ctx =>
            {
                var cle = ctx.Ch("cle", "ma_variable");
                var defaut = ctx.Ch("defaut", "");
                if (string.IsNullOrEmpty(cle))
                    return Task.FromResult(ResultatExecution.Fail("variable_get: cle vide"));
                lock (_lock)
                {
                    if (_store.TryGetValue(cle, out var v))
                        return Task.FromResult(ResultatExecution.Ok(new() { ["valeur"] = v, ["existe"] = true }));
                }
                return Task.FromResult(ResultatExecution.Ok(new() { ["valeur"] = defaut, ["existe"] = false }));
            }
        ));

        // 25. variable_compteur
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "variable_compteur", "variable_compteur",
            "Incremente (ou decremente) un compteur nomme et retourne la nouvelle valeur. Premier appel = la valeur de depart.",
            Espace.Codage, "Variables",
            new List<Port>(),
            new List<Port>
            {
                new("valeur", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("cle", "Cle", "texte", "compteur"),
                new("pas", "Pas (peut etre negatif)", "nombre", 1),
                new("depart", "Valeur de depart", "nombre", 0),
            },
            ctx =>
            {
                var cle = ctx.Ch("cle", "compteur");
                var pas = ctx.ChInt("pas", 1);
                var depart = ctx.ChDouble("depart", 0);
                if (string.IsNullOrEmpty(cle))
                    return Task.FromResult(ResultatExecution.Fail("variable_compteur: cle vide"));
                double r;
                lock (_lock)
                {
                    if (_store.TryGetValue(cle, out var prev) && double.TryParse(prev, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pd))
                        r = pd + pas;
                    else
                        r = depart;
                    _store[cle] = r.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                return Task.FromResult(ResultatExecution.Ok(new() { ["valeur"] = r }));
            }
        ));
    }
}
