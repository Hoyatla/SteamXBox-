namespace SenSÉ.Atelier.Modele;

public enum Espace
{
    Codage,
    Multimedia,
}

public static class EspaceExtensions
{
    public static string Id(this Espace e) => e switch
    {
        Espace.Codage     => "codage",
        Espace.Multimedia => "multimedia",
        _                 => "inconnu",
    };

    public static string Libelle(this Espace e) => e switch
    {
        Espace.Codage     => "Codage",
        Espace.Multimedia => "Multimédia",
        _                 => "Inconnu",
    };

    public static bool TryParse(string s, out Espace e)
    {
        switch (s)
        {
            case "codage":     e = Espace.Codage;     return true;
            case "multimedia": e = Espace.Multimedia; return true;
            default:           e = Espace.Codage;     return false;
        }
    }
}