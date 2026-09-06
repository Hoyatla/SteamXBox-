using System.Windows;
using SenSÉ.Atelier.Modele;
using SenSÉ.Atelier.Mcp;

namespace SenSÉ.Atelier;

public partial class App : Application
{
    public Mcp.ServeurHttp? Serveur { get; set; }
    public string? Racine { get; set; }
    public Espace EspaceCourant { get; set; } = Espace.Codage;

    public App()
    {
        DispatcherUnhandledException += (s, e) =>
        {
            System.Console.Error.WriteLine("[atelier] UNHANDLED: " + e.Exception);
            e.Handled = true;
        };
    }
}