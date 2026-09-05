using SenSÉ.Tools.Generation;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// Le passage entre le dossier de l'utilisateur et les entrées du générateur.
/// </summary>
/// <remarks>
/// <b>C'est le chaînon qui manquait entre « donnez-moi le chemin » et « je lance ».</b> L'assistant
/// demandait un dossier, le recevait, et s'arrêtait : le générateur ne lit que sous ses propres
/// entrées, et un chemin comme <c>D:\mes photos</c> n'y désigne rien. Le dépôt existait pour les
/// fichiers isolés d'un flux, jamais pour un dossier entier — donc jamais pour le seul cas où
/// l'utilisateur en a un.
/// </remarks>
public class DepotDossierTests : IDisposable
{
    private readonly DirectoryInfo _bac = Directory.CreateTempSubdirectory("depot");
    private readonly List<string> _deposes = [];

    /// <summary>Les entrées du générateur, telles que le dépôt les calcule.</summary>
    private static string Entrees
        => Path.Combine(AppContext.BaseDirectory, "Outils", "ComfyUI", "input");

    public void Dispose()
    {
        _bac.Delete(recursive: true);

        // Le dépôt écrit hors du bac, dans les entrées du générateur : c'est tout son sujet. Sans
        // ce nettoyage, chaque exécution de l'épreuve laisserait des dossiers derrière elle, et le
        // test « redéposer ne change rien » finirait par passer pour de mauvaises raisons.
        foreach (var nom in _deposes)
        {
            var chemin = Path.Combine(Entrees, nom);

            if (Directory.Exists(chemin))
            {
                Directory.Delete(chemin, recursive: true);
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Dépose, et retient le nom rendu pour le retirer à la fin.</summary>
    private string Deposer(string source)
    {
        var rendu = SequenceAnimee.Deposer(source, null);

        if (!rendu.StartsWith("Impossible", StringComparison.Ordinal))
        {
            _deposes.Add(rendu);
        }

        return rendu;
    }

    private string Dossier(string nom, params string[] fichiers)
    {
        var chemin = Path.Combine(_bac.FullName, nom);

        Directory.CreateDirectory(chemin);

        foreach (var fichier in fichiers)
        {
            File.WriteAllText(Path.Combine(chemin, fichier), fichier);
        }

        return chemin;
    }

    /// <summary>Un dossier qui n'existe pas est refusé, en disant quoi demander.</summary>
    /// <remarks>
    /// Le refus doit se lire par le modèle : c'est lui qui reçoit la phrase, et c'est lui qui doit
    /// savoir qu'il faut redemander le chemin plutôt que réessayer le même.
    /// </remarks>
    [Fact]
    public void AFolderThatDoesNotExistIsRefused()
    {
        var refus = Deposer(Path.Combine(_bac.FullName, "absent"));

        Assert.StartsWith("Impossible", refus, StringComparison.Ordinal);
        Assert.Contains("dossier", refus, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Un dossier sans image est refusé, et les formats lus sont nommés.</summary>
    /// <remarks>
    /// Sans la liste des formats, l'utilisateur dont le dossier ne contient que des HEIC ou des TIFF
    /// ne peut pas savoir si le produit refuse son dossier ou ses fichiers.
    /// </remarks>
    [Fact]
    public void AFolderWithoutImagesIsRefused()
    {
        var refus = Deposer(Dossier("vide", "notes.txt", "flux.json"));

        Assert.StartsWith("Impossible", refus, StringComparison.Ordinal);
        Assert.Contains("PNG", refus, StringComparison.Ordinal);
    }

    /// <summary>Le nom rendu est préfixé, pour ne pas recouvrir un dossier du serveur.</summary>
    /// <remarks>
    /// Le dossier des entrées appartient au générateur, et l'utilisateur y a peut-être les siens.
    /// Recouvrir son <c>photos</c> parce qu'il en a un du même nom serait une perte silencieuse —
    /// la pire espèce, puisqu'elle ne se découvre qu'en cherchant un fichier qui n'est plus là.
    /// </remarks>
    [Fact]
    public void TheReturnedNameIsPrefixed()
        => Assert.StartsWith(
            "SenSÉ-",
            Deposer(Dossier("photos", "a.png")),
            StringComparison.Ordinal);

    /// <summary>Les caractères qu'un système de fichiers refuse sont réduits.</summary>
    /// <remarks>
    /// Le nom vient d'un dossier choisi par l'utilisateur, qui peut porter des espaces, des accents
    /// ou des signes. Il devient un nom de dossier réel côté générateur, puis une valeur passée dans
    /// une cible découpée aux barres verticales : ce qui n'est ni lettre ni chiffre n'y survit pas.
    ///
    /// <para>
    /// Les accents, eux, restent : ce sont des lettres. Les réduire aurait rendu <c>été 2026</c> en
    /// <c>-t--2026</c>, méconnaissable dans un dossier d'entrées où l'utilisateur doit pouvoir
    /// retrouver ce qu'il a déposé.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("mes photos", "SenSÉ-mes-photos")]
    [InlineData("été 2026", "SenSÉ-été-2026")]
    [InlineData("---", "SenSÉ-images")]
    public void AwkwardNamesAreReduced(string nom, string attendu)
        => Assert.Equal(attendu, Deposer(Dossier(nom, "a.png")));

    /// <summary>Seules les images sont copiées ; le reste du dossier ne suit pas.</summary>
    [Fact]
    public void OnlyImagesAreCopied()
    {
        var depose = Path.Combine(
            Entrees, Deposer(Dossier("melange", "a.png", "b.jpg", "notes.txt", "flux.json")));

        Assert.Equal(2, Directory.GetFiles(depose).Length);
        Assert.False(File.Exists(Path.Combine(depose, "notes.txt")));
    }

    /// <summary>Redéposer le même dossier deux fois est sans effet.</summary>
    /// <remarks>
    /// Un second essai doit être gratuit, pas dangereux. C'est la même règle que pour les fichiers
    /// d'un flux, et elle a été apprise de la même façon : recopier pendant qu'une génération tient
    /// le fichier ouvert faisait échouer la copie, alors que le fichier était déjà là et identique.
    /// </remarks>
    [Fact]
    public void DepositingTwiceChangesNothing()
    {
        var source = Dossier("stable", "a.png", "b.png");

        var premier = Deposer(source);
        var second = Deposer(source);

        Assert.Equal(premier, second);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Entrees, premier)).Length);
    }
}
