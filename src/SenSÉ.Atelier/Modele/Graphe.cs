using System;
using System.Collections.Generic;
using System.Linq;

namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Un graphe = un DAG de noeuds relies. Plusieurs onglets par espace.
/// Phase 3 : ajoute Groupes (cadres colores) et Commentaires (annotations libres).
/// </summary>
public sealed class Graphe
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public Espace Espace { get; set; } = Espace.Codage;
    public string Nom { get; set; } = "Sans nom";
    public DateTime ModifieLe { get; set; } = DateTime.UtcNow;
    public List<Noeud> Noeuds { get; set; } = new();
    public List<Lien> Liens { get; set; } = new();
    /// <summary>Cadres colores (Phase 3.5).</summary>
    public List<Groupe> Groupes { get; set; } = new();
    /// <summary>Annotations libres (Phase 3.6).</summary>
    public List<Commentaire> Commentaires { get; set; } = new();

    public Noeud? TrouverNoeud(string id) => Noeuds.FirstOrDefault(n => n.Id == id);
    public Lien? TrouverLien(string id) => Liens.FirstOrDefault(l => l.Id == id);
}
