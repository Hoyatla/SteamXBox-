using System;
using System.Collections.Generic;
using System.Globalization;
using SenSÉ.Atelier.Execution;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Bibliotheque.Codage;

public static class Dates
{
    public static void Enregistrer()
    {
        // 18. date_maintenant
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "date_maintenant",
            "date_maintenant",
            "Date et heure actuelles, dans le format choisi (ISO 8601, francais, timestamp, humain).",
            Espace.Codage,
            "Dates",
            new List<Port>(),
            new List<Port> { new("valeur", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("format", "Format", "liste", "iso8601", new List<string> { "iso8601", "french", "timestamp", "humain" }),
            },
            async ctx =>
            {
                var format = ctx.Ch("format", "iso8601");
                var now = DateTime.Now;
                string r;
                if (format == "iso8601") r = now.ToString("o", CultureInfo.InvariantCulture);
                else if (format == "french") r = now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.GetCultureInfo("fr-FR"));
                else if (format == "timestamp") r = ((DateTimeOffset)now).ToUnixTimeSeconds().ToString();
                else if (format == "humain") r = now.ToString("dddd d MMMM yyyy 'a' HH:mm", CultureInfo.GetCultureInfo("fr-FR"));
                else r = now.ToString(CultureInfo.InvariantCulture);
                return ResultatExecution.Ok(new() { ["valeur"] = r });
            }
        ));

        // 19. date_parser
        CatalogueNoeuds.Enregistrer(new DefinitionNoeud(
            "date_parser",
            "date_parser",
            "Parse une date (plusieurs formats) et la retourne en ISO 8601. 'auto' essaie plusieurs heuristiques.",
            Espace.Codage,
            "Dates",
            new List<Port> { new("texte", TypePort.Texte, true) },
            new List<Port> { new("iso", TypePort.Texte, false) },
            new List<ParametreNoeud>
            {
                new("format_attendu", "Format attendu", "liste", "auto",
                    new List<string> { "auto", "iso8601", "rfc2822", "dd/MM/yyyy", "MM/dd/yyyy" }),
            },
            async ctx =>
            {
                var texte = ctx.Entree("texte") ?? "";
                if (string.IsNullOrEmpty(texte))
                    return ResultatExecution.Fail("date_parser: texte vide");
                var fmt = ctx.Ch("format_attendu", "auto");
                DateTime? dt = null;
                try
                {
                    if (fmt == "iso8601")
                        dt = DateTime.ParseExact(texte, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                    else if (fmt == "rfc2822")
                        dt = DateTime.ParseExact(texte, "ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture);
                    else if (fmt == "dd/MM/yyyy")
                        dt = DateTime.ParseExact(texte, "dd/MM/yyyy", CultureInfo.InvariantCulture);
                    else if (fmt == "MM/dd/yyyy")
                        dt = DateTime.ParseExact(texte, "MM/dd/yyyy", CultureInfo.InvariantCulture);
                    else if (fmt == "auto")
                        dt = ParseAuto(texte);
                }
                catch { dt = null; }
                if (dt is null)
                    return ResultatExecution.Fail("date_parser: format '" + fmt + "' n'a pas parse '" + texte + "'");
                return ResultatExecution.Ok(new() { ["iso"] = dt.Value.ToString("o", CultureInfo.InvariantCulture) });
            }
        ));
    }

    private static DateTime? ParseAuto(string s)
    {
        string[] formats = { "o", "s", "yyyy-MM-dd", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
        foreach (var f in formats)
        {
            if (DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                return dt;
        }
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt2))
            return dt2;
        return null;
    }
}
