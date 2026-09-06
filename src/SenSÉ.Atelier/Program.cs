using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using SenSÉ.Atelier.Mcp;
using SenSÉ.Atelier.UI;

namespace SenSÉ.Atelier;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var racine = DeterminerRacine();
        Console.Error.WriteLine($"[atelier] racine: {racine}");

        var port = 8770;
        foreach (var a in args)
        {
            if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                int.TryParse(a[7..], out port);
        }

        // Demarrer le serveur HTTP en arriere-plan
        var serveur = new ServeurHttp(racine, port);
        var t = Task.Run(async () =>
        {
            try { await serveur.DemarrerAsync(); }
            catch (Exception ex) { Console.Error.WriteLine("[atelier] serveur: " + ex.Message); }
        });

        // Lancer l'application WPF
        var app = new App();
        app.InitializeComponent();
        app.Serveur = serveur;
        app.Racine = racine;
        var exit = app.Run(new FenetreAtelier(racine, serveur));

        serveur.Arreter();
        return exit;
    }

    private static string DeterminerRacine()
    {
        // 1. Variable d'environnement
        var env = Environment.GetEnvironmentVariable("ATELIER_RACINE");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

        // 2. A cote de l'exe : Outils/Atelier/
        var exeDir = AppContext.BaseDirectory;
        var outils = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "Outils", "Atelier"));
        if (Directory.Exists(outils)) return outils;
        // Cas du publish self-contained
        var outilsPub = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "Outils", "Atelier"));
        if (Directory.Exists(outilsPub)) return outilsPub;

        // 3. Fallback : a cote de l'exe
        return Path.Combine(exeDir, "Atelier");
    }
}