using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Langages;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

/// <summary>
/// Le noeud unique d execution de code. Douze langages au lieu de douze noeuds.
/// </summary>
/// <remarks>
/// <b>Le parametre <c>langage</c> est alimente a l execution</b> par les moteurs
/// effectivement detectes sur la machine (cf. <see cref="Detecteur.Moteurs"/>).
/// Plus la valeur speciale <c>auto</c> : le moteur le moins cher qui peut
/// executer le code est choisi, et son id est inscrit dans le noeud (etape 4).
///
/// <para><b>Repertoire d execution isole</b> : chaque lancement cree
/// <c>Outils/Atelier/Executions/&lt;uuid&gt;/</c> et y ecrit le source. Pas d ecriture
/// dans le dossier de l utilisateur, pas de collision entre graphes.</para>
/// </remarks>
public static class ExecuterCode
{
    public const string LangageAuto = "auto";

    public static void Enregistrer()
    {
        var langages = new List<string> { LangageAuto };
        langages.AddRange(Detecteur.Moteurs.Select(m => m.Id));
        var listeLangs = string.Join(",", langages);

        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "executer_code", "Executer du code", "Execute un code source dans le langage choisi (ou auto).",
            Espace.Codage, "Execution",
            new List<Port> { new("code", TypePort.Texte, true) },
            new List<Port>
            {
                new("stdout", TypePort.Texte, false),
                new("stderr", TypePort.Texte, false),
                new("code_retour", TypePort.Nombre, false),
                new("duree_ms", TypePort.Nombre, false),
            },
            new List<ParametreNoeud>
            {
                new("langage", "Langage", "liste", LangageAuto, langages),
                new("limite_s", "Limite (s)", "nombre", 30.0),
                new("dossier_travail", "Dossier de travail (optionnel)", "texte", ""),
            },
            async ctx =>
            {
                try
                {
                    var code = ctx.Entree("code") ?? ctx.Ch("code");
                    if (string.IsNullOrEmpty(code)) return ResultatExecution.Fail("code vide");
                    var lang = ctx.Ch("langage", LangageAuto);
                    var limite = ctx.ChInt("limite_s", 30);

                    MoteurSpec? moteur;
                    if (lang == LangageAuto)
                    {
                        moteur = Selectionneur.Choisir(code, Detecteur.Moteurs);
                        if (moteur is null) return ResultatExecution.Fail("aucun moteur disponible sur cette machine");
                        // Etape 4 : inscrire le choix dans le noeud
                        ctx.Params["langage"] = moteur.Id;
                    }
                    else
                    {
                        moteur = Detecteur.Trouver(lang);
                        if (moteur is null)
                            return ResultatExecution.Fail("langage demande absent de cette machine : " + lang);
                    }

                    var res = await MoteurExecuteur.ExecuterAsync(moteur, code, limite, ctx.Annulation);
                    return ResultatExecution.Ok(new()
                    {
                        ["stdout"] = res.Stdout,
                        ["stderr"] = res.Stderr,
                        ["code_retour"] = res.CodeRetour,
                        ["duree_ms"] = res.DureeMs,
                    });
                }
                catch (Exception ex) { return ResultatExecution.Fail("executer_code: " + ex.Message); }
            }
        ));
    }
}