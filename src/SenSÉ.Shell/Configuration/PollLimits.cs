namespace SenSÉ.Shell.Configuration;

public sealed partial class AppSettings
{
    // Bornes du sondage de peripherique, en secondes. Ici et pas a cote du curseur qui les affiche :
    // l'ecran de reglages de Desktop les propose, et la boucle de detection du GUI s'y tient. Deux
    // processus, une seule verite. Le plafond de 10 s existe parce qu'un reglage plus lent donnait
    // l'impression que la manette ne se connectait plus du tout.
    public const int MinPollSeconds = 1;
    public const int MaxPollSeconds = 10;
}