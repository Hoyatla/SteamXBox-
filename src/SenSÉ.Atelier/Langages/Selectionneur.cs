using System;
using System.Collections.Generic;
using System.Linq;

namespace SenSÉ.Atelier.Langages;

/// <summary>
/// Regle de selection <c>auto</c> : choisit le moteur le moins cher (latence) qui
/// peut executer le code. Heuristique par mots-cles.
/// </summary>
/// <remarks>
/// <b>Pas d LLM ici.</b> Le modele recoit deja la liste des moteurs disponibles
/// avec leur latence (cf. consigne de l Assistant, etape 4). Cette classe est le
/// filet de securite : si le modele n a pas precise, on choisit selon une table de
/// mots-cles triviale, et on inscrit le choix dans le noeud.
/// </remarks>
public static class Selectionneur
{
    /// <summary>Heuristique : retourne le premier id de moteur qui matche, ou le moins cher.</summary>
    public static MoteurSpec? Choisir(string code, IReadOnlyList<MoteurSpec> disponibles)
    {
        if (disponibles.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(code)) return disponibles[0];

        // Heuristique : mot-cle -> id de langage
        var c = code.ToLowerInvariant();
        string? id = null;
        if (Contient(c, "fn main", "use std::", "let mut ", "->", "impl ")) id = "rust";
        else if (Contient(c, "console.log", "const ", "=> {", "require(")) id = "node";
        else if (Contient(c, "console.writeline", "static void main", "string[] args", "namespace ")) id = "csharp";
        else if (Contient(c, "system.out.println", "public static void main", "string[]")) id = "java";
        else if (Contient(c, "int main(", "printf(", "#include <")) id = "c";
        else if (Contient(c, "def ", "import ", "print(", "if __name__")) id = "python";

        if (id is not null)
        {
            var m = disponibles.FirstOrDefault(x => x.Id == id);
            if (m is not null) return m;
        }
        // Fallback : le moins cher
        return disponibles[0];
    }

    private static bool Contient(string haystack, params string[] needles)
        => needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}