using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using SenSÉ.Atelier.Modele;

namespace SenSÉ.Atelier.Custom;

/// <summary>
/// Un noeud defini par l'utilisateur : stocke en JSON, execute en
/// faisant tourner un script Python.
/// </summary>
public sealed class NoeudCustom
{
    [JsonPropertyName("id")]            public string Id { get; set; } = "";
    [JsonPropertyName("nom")]           public string Nom { get; set; } = "";
    [JsonPropertyName("espace")]        public string Espace { get; set; } = "codage";
    [JsonPropertyName("description")]   public string Description { get; set; } = "";
    [JsonPropertyName("categorie")]     public string Categorie { get; set; } = "Perso";
    [JsonPropertyName("ports_entree")]  public List<PortDto> PortsEntree { get; set; } = new();
    [JsonPropertyName("ports_sortie")]  public List<PortDto> PortsSortie { get; set; } = new();
    [JsonPropertyName("params")]        public List<ParamDto> Params { get; set; } = new();
    [JsonPropertyName("langage")]       public string Langage { get; set; } = "python";
    [JsonPropertyName("code")]          public string Code { get; set; } = "";

    public sealed class PortDto
    {
        [JsonPropertyName("nom")]  public string Nom { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "Texte";
    }

    public sealed class ParamDto
    {
        [JsonPropertyName("nom")]    public string Nom { get; set; } = "";
        [JsonPropertyName("libelle")]public string Libelle { get; set; } = "";
        [JsonPropertyName("type")]   public string Type { get; set; } = "texte";
        [JsonPropertyName("defaut")] public object? Defaut { get; set; }
    }
}