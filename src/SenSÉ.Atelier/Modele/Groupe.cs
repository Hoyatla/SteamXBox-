using System;
using System.Collections.Generic;

namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Groupe visuel : un cadre colore autour d'un ensemble de noeuds. 5 couleurs.
/// </summary>
public sealed class Groupe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Couleur { get; set; } = "blue"; // red, orange, yellow, green, blue
    public string? Label { get; set; }
    public List<string> NoeudIds { get; set; } = new();
}
