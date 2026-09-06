namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Types de donnees qui peuvent transiter par un port ou un champ de parametre.
/// Le moteur refuse un lien entre deux ports de types incompatibles.
/// </summary>
public enum TypePort
{
    Texte,
    Nombre,
    Booleen,
    Image,
    Video,
    Audio,
    Fichier,
    Json,
    Liste,
    Aucun,
}

public static class TypePortExtensions
{
    public static string Libelle(this TypePort t) => t switch
    {
        TypePort.Texte    => "Texte",
        TypePort.Nombre   => "Nombre",
        TypePort.Booleen  => "Booléen",
        TypePort.Image    => "Image",
        TypePort.Video    => "Vidéo",
        TypePort.Audio    => "Audio",
        TypePort.Fichier  => "Fichier",
        TypePort.Json     => "JSON",
        TypePort.Liste    => "Liste",
        _                 => "—",
    };

    public static string Couleur(this TypePort t) => t switch
    {
        TypePort.Texte    => "#6BB6FF",
        TypePort.Nombre   => "#F2C14E",
        TypePort.Booleen  => "#B388FF",
        TypePort.Image    => "#7EE787",
        TypePort.Video    => "#FF9F4A",
        TypePort.Audio    => "#FF6E9C",
        TypePort.Fichier  => "#C5C5C5",
        TypePort.Json     => "#A0A0A0",
        TypePort.Liste    => "#52D1DC",
        _                 => "#808080",
    };

    public static bool Compatible(this TypePort source, TypePort cible)
    {
        if (source == cible) return true;
        if (source == TypePort.Aucun || cible == TypePort.Aucun) return true;
        if ((source == TypePort.Texte && cible == TypePort.Fichier) ||
            (source == TypePort.Fichier && cible == TypePort.Texte)) return true;
        if ((source == TypePort.Json && cible == TypePort.Texte) ||
            (source == TypePort.Texte && cible == TypePort.Json)) return true;
        return false;
    }
}