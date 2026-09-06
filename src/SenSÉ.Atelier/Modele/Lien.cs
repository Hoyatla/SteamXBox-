namespace SenSÉ.Atelier.Modele;

/// <summary>
/// Un lien entre un port de sortie d'un noeud source et un port d'entree
/// d'un noeud cible. Les IDs sont stockes sous la forme "{noeud_id}|{port_nom}"
/// pour eviter toute ambiguite.
/// </summary>
public sealed class Lien
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string NoeudSourceId { get; set; } = "";
    public string PortSourceNom { get; set; } = "";
    public string NoeudCibleId { get; set; } = "";
    public string PortCibleNom { get; set; } = "";

    public override string ToString()
        => $"{NoeudSourceId}.{PortSourceNom} -> {NoeudCibleId}.{PortCibleNom}";
}