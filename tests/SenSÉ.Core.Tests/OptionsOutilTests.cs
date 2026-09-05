using SenSÉ.Plugins;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Ce qui sépare le mot que l'utilisateur lit du chiffre que l'outil reçoit.
/// </summary>
/// <remarks>
/// <b>Le défaut que cela corrige.</b> Un panneau demandait « Mouvement, entre 1 et 255 ». Personne
/// ne sait ce que vaut 127, ni qu'au-delà de 200 l'image se déforme au point de n'avoir plus de
/// sens. Le réglage était à la fois incompréhensible et piégeux — deux défauts qui se soignent
/// ensemble en écrivant <c>60=Léger</c>, <c>127=Modéré</c>, <c>180=Ample</c> : trois mots qu'on
/// comprend, et la zone qui casse simplement absente.
/// </remarks>
public class OptionsOutilTests
{
    private static readonly string[] Mouvement =
        ["60=Léger", "127=Modéré", "180=Ample"];

    [Fact]
    public void AnOptionCarriesAWordAndAValue()
    {
        var lu = OptionsOutil.Lire("180=Ample");

        Assert.Equal("180", lu.Valeur);
        Assert.Equal("Ample", lu.Libelle);
    }

    /// <summary>Une option sans signe égal reste elle-même.</summary>
    /// <remarks>
    /// Les listes lues sur la machine — les modèles installés, les extensions d'un fichier — n'ont
    /// pas de libellé et n'en veulent pas : un nom de fichier est déjà ce qu'il faut montrer. La
    /// règle ne coûte donc rien aux manifestes qui existaient avant elle.
    /// </remarks>
    [Fact]
    public void APlainOptionStaysItself()
    {
        var lu = OptionsOutil.Lire("flux1-schnell-Q5_K_S.gguf");

        Assert.Equal("flux1-schnell-Q5_K_S.gguf", lu.Valeur);
        Assert.Equal("flux1-schnell-Q5_K_S.gguf", lu.Libelle);
    }

    /// <summary>Le premier signe égal seulement.</summary>
    /// <remarks>
    /// Un libellé peut en contenir. Découper sur le dernier, ou sur tous, produirait une valeur
    /// fausse là où l'auteur du manifeste croyait ne rien changer.
    /// </remarks>
    [Fact]
    public void OnlyTheFirstEqualsCounts()
    {
        var lu = OptionsOutil.Lire("1024=Standard = carré");

        Assert.Equal("1024", lu.Valeur);
        Assert.Equal("Standard = carré", lu.Libelle);
    }

    [Theory]
    [InlineData("=Ample")]
    [InlineData("180=")]
    [InlineData("")]
    public void AMalformedOptionIsTakenWhole(string ecrit)
        => Assert.Equal(ecrit.Trim(), OptionsOutil.Lire(ecrit).Valeur);

    /// <summary>Le libellé comme la valeur mènent à la même chose.</summary>
    /// <remarks>
    /// L'utilisateur choisit « Ample » dans une liste ; l'assistant peut employer l'un ou l'autre
    /// selon ce qu'il a retenu de la déclaration. Lui refuser « 180 » parce qu'on attendait
    /// « Ample » serait une pédanterie qui casse une demande parfaitement claire.
    /// </remarks>
    [Theory]
    [InlineData("Ample", "180")]
    [InlineData("ample", "180")]
    [InlineData("180", "180")]
    [InlineData("Léger", "60")]
    public void BothTheWordAndTheValueLeadToTheValue(string dit, string attendu)
        => Assert.Equal(attendu, OptionsOutil.Valeur(Mouvement, dit));

    /// <summary>Ce qui ne correspond à rien traverse inchangé.</summary>
    /// <remarks>
    /// Un champ dont les options viennent de la machine peut recevoir un nom de fichier que la
    /// liste ne portait pas encore ; le refuser ici masquerait la vraie erreur, que l'arbitre du
    /// flux dira bien mieux et en nommant le nœud.
    /// </remarks>
    [Fact]
    public void WhatMatchesNothingPassesThrough()
        => Assert.Equal("255", OptionsOutil.Valeur(Mouvement, "255"));

    [Fact]
    public void AValueIsShownUnderItsWord()
    {
        Assert.Equal("Modéré", OptionsOutil.Libelle(Mouvement, "127"));
        Assert.Equal("999", OptionsOutil.Libelle(Mouvement, "999"));
    }

    /// <summary>Ce qui casse n'est pas proposé.</summary>
    /// <remarks>
    /// Le modèle accepte jusqu'à 255, et au-delà de 180 l'image se déforme au point de ne plus rien
    /// représenter. Une liberté qui ne mène qu'à un résultat inutilisable n'est pas une liberté.
    /// </remarks>
    [Fact]
    public void WhatBreaksIsNotOffered()
    {
        var propose = OptionsOutil.Lire(Mouvement).Select(o => int.Parse(o.Valeur)).ToList();

        Assert.All(propose, v => Assert.InRange(v, 1, 180));
    }
}
