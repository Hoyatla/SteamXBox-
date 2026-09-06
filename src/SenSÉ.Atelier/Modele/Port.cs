namespace SenSÉ.Atelier.Modele;

public sealed class Port
{
    public string Nom { get; set; } = "";
    public TypePort Type { get; set; } = TypePort.Texte;
    public bool EstEntree { get; set; }

    public Port() { }
    public Port(string nom, TypePort type, bool entree)
    {
        Nom = nom;
        Type = type;
        EstEntree = entree;
    }

    public override string ToString() => $"{(EstEntree ? "in" : "out")}.{Nom}:{Type}";
}