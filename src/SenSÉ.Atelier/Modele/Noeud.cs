using System.Collections.Generic;

namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Un noeud dans un graphe. Possede un type (registre), des coordonnees
/// (x, y) sur le canvas, des params, des ports d'entree et de sortie.
/// </summary>
public sealed class Noeud
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, object?> Params { get; set; } = new();
    public List<Port> PortsEntree { get; set; } = new();
    public List<Port> PortsSortie { get; set; } = new();

    public Noeud Clone()
    {
        var n = new Noeud
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = Type,
            X = X + 30,
            Y = Y + 30,
            Params = new Dictionary<string, object?>(Params),
            PortsEntree = new List<Port>(PortsEntree),
            PortsSortie = new List<Port>(PortsSortie),
        };
        return n;
    }
}