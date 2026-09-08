using System;

namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Commentaire / annotation libre sur le canvas. Pas lie a un noeud.
/// 3 tailles : 12, 16, 24 (pt).
/// </summary>
public sealed class Commentaire
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Texte { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>12, 16 ou 24 (pt).</summary>
    public int Taille { get; set; } = 12;
    public string Couleur { get; set; } = "#F5E10B"; // jaune par defaut
}
