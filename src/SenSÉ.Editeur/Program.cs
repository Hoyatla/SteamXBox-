using System;
using System.Windows;

namespace SenSÉ.Editeur;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
