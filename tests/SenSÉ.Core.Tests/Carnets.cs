using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Les épreuves qui touchent au dossier des carnets, exécutées l'une après l'autre.
/// </summary>
/// <remarks>
/// <b>Elles partagent un statique, et xUnit exécute les classes en parallèle.</b> Deux classes
/// réglant chacune <c>FichierTravail.Racine</c> sur son propre bac se les volaient l'une à l'autre :
/// une épreuve écrivait dans le dossier de sa voisine, puis ne retrouvait plus son carnet. Le
/// symptôme était un échec intermittent, qui change de victime à chaque exécution — le pire genre.
///
/// <para>
/// La vraie cause est ce statique modifiable, introduit pour la commodité des épreuves. Le
/// sérialiser est le remède qui coûte une ligne ; le supprimer demanderait de passer le dossier à
/// chacune des sept méthodes du magasin, pour le seul confort des tests.
/// </para>
/// </remarks>
[CollectionDefinition(Nom, DisableParallelization = true)]
public sealed class Carnets
{
    public const string Nom = "carnets";
}
