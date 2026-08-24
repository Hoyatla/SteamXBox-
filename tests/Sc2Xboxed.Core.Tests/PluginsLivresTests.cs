using System.Text.Json;
using SteamXBox.Plugins;
using SteamXBox.Tools.Generation;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// Les manifestes réellement livrés, lus comme le produit les lira.
/// </summary>
/// <remarks>
/// Les autres épreuves construisent leurs manifestes en mémoire, ce qui vérifie les règles mais
/// jamais les fichiers. Or un manifeste est du texte : une virgule de trop, un identifiant renommé,
/// et l'outil disparaît de la grille au démarrage suivant sans rien casser ailleurs. Le journal le
/// dit, mais personne ne lit le journal quand tout va bien.
///
/// <para>
/// <b>La dérive entre un manifeste et son flux est le cas qui motive tout ceci.</b> Un flux de
/// travail est un fichier que quelqu'un peut rouvrir dans l'éditeur, renuméroter et réenregistrer,
/// pendant que le manifeste continue de nommer les anciens nœuds. Les deux fichiers restent
/// valides chacun de son côté ; c'est leur accord qui a disparu, et rien ne le dirait avant le clic
/// d'un utilisateur.
/// </para>
/// </remarks>
public class PluginsLivresTests
{
    /// <summary>La racine du dépôt, trouvée en remontant depuis les binaires de l'épreuve.</summary>
    private static string Racine()
    {
        var ou = new DirectoryInfo(AppContext.BaseDirectory);

        while (ou is not null && !Directory.Exists(Path.Combine(ou.FullName, "Plugins")))
        {
            ou = ou.Parent;
        }

        Assert.True(ou is not null, "dossier 'Plugins' introuvable au-dessus des binaires d'épreuve");

        return ou!.FullName;
    }

    /// <summary>Tous les manifestes livrés passent le catalogue.</summary>
    /// <remarks>
    /// Un manifeste refusé ne fait pas échouer le démarrage : il est écarté et l'outil n'apparaît
    /// pas. C'est le bon comportement au moment de l'exécution — un plugin cassé ne doit pas
    /// emporter l'environnement — et c'est précisément pour cela qu'il faut le constater ici.
    /// </remarks>
    [Fact]
    public void EveryShippedManifestIsAccepted()
    {
        var refuses = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Rejected;

        Assert.True(
            refuses.Count == 0,
            string.Join("; ", refuses.Select(r => $"{r.Directory} : {r.Reason}")));
    }

    /// <summary>Aucun réglage livré ne dit « à laisser tel quel » au premier plan.</summary>
    /// <remarks>
    /// <b>C'est la règle que la refonte des panneaux a posée.</b> Un réglage dont l'explication
    /// commence par « à laisser tel quel » n'est pas un choix, c'est une conséquence : le montrer au
    /// même rang que la description invite à casser quelque chose sans rien offrir en échange. Il
    /// doit être marqué <c>avance</c>, donc rangé derrière un repli.
    /// </remarks>
    [Fact]
    public void NoFrontRankSettingTellsTheUserNotToTouchIt()
    {
        var fautifs = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Loaded
            .SelectMany(m => m.Content.Select(c => (Outil: m.Id, Champ: c)))
            .Where(x => !x.Champ.Avance
                        && x.Champ.Hint.Contains("laisser tel quel", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.Outil}.{x.Champ.Id}")
            .ToList();

        Assert.True(fautifs.Count == 0, string.Join(", ", fautifs));
    }

    /// <summary>Un réglage avancé garde une explication : c'est là qu'elle sert le plus.</summary>
    /// <remarks>
    /// Ce qui est rangé derrière un repli n'est déplié que par quelqu'un qui se demande à quoi ça
    /// sert. Un champ avancé sans explication est le seul cas où l'on est certain que la question
    /// se posera.
    /// </remarks>
    [Fact]
    public void EveryAdvancedSettingExplainsItself()
    {
        var muets = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Loaded
            .SelectMany(m => m.Content.Select(c => (Outil: m.Id, Champ: c)))
            .Where(x => x.Champ.Avance && x.Champ.Hint.Length == 0)
            .Select(x => $"{x.Outil}.{x.Champ.Id}")
            .ToList();

        Assert.True(muets.Count == 0, string.Join(", ", muets));
    }

    /// <summary>Aucun choix livré ne propose une seule option.</summary>
    /// <remarks>
    /// Une liste à une entrée n'est pas un choix : elle occupe une ligne du panneau, se déplie pour
    /// rien, et laisse croire qu'il y avait une décision à prendre. Si une seule valeur tient, elle
    /// se met dans la cible et s'explique dans l'aide de l'outil.
    /// </remarks>
    [Fact]
    public void NoShippedChoiceOffersASingleOption()
    {
        var seuls = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Loaded
            .SelectMany(m => m.Content.Select(c => (Outil: m.Id, Champ: c)))
            .Where(x => x.Champ.Kind.Equals("choice", StringComparison.OrdinalIgnoreCase)
                        && x.Champ.From.Length == 0
                        && x.Champ.Options.Count == 1)
            .Select(x => $"{x.Outil}.{x.Champ.Id}")
            .ToList();

        Assert.True(seuls.Count == 0, string.Join(", ", seuls));
    }

    /// <summary>Un classeur ne nomme que des outils qui existent.</summary>
    /// <remarks>
    /// Un identifiant mal orthographié dans la cible ne casse rien : l'onglet manque, et le journal
    /// le dit à qui le lit. L'utilisateur, lui, voit un classeur amputé sans savoir pourquoi — et
    /// l'outil disparu de la grille, puisque le classeur était censé l'y remplacer, n'est alors
    /// atteignable nulle part.
    /// </remarks>
    [Fact]
    public void AWorkbenchOnlyNamesToolsThatExist()
    {
        var outils = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Loaded;
        var connus = outils.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manques = new List<string>();

        foreach (var classeur in outils.Where(m =>
                     m.Does.Equals(PluginActions.Atelier, StringComparison.OrdinalIgnoreCase)))
        {
            manques.AddRange(PluginActions.Reunis(classeur.Target)
                .Where(id => !connus.Contains(id))
                .Select(id => $"{classeur.Id} réunit « {id} », qui n'existe pas"));
        }

        Assert.True(manques.Count == 0, string.Join("\n", manques));
    }

    /// <summary>Aucun outil n'est réuni par deux classeurs.</summary>
    /// <remarks>
    /// Il perdrait sa tuile une fois et apparaîtrait deux fois en onglet, avec deux panneaux vivants
    /// pour le même outil — dont un seul serait celui que l'assistant retrouve.
    /// </remarks>
    [Fact]
    public void NoToolIsGatheredTwice()
    {
        var reunis = PluginCatalog.Scan(Path.Combine(Racine(), "Plugins")).Loaded
            .Where(m => m.Does.Equals(PluginActions.Atelier, StringComparison.OrdinalIgnoreCase))
            .SelectMany(m => PluginActions.Reunis(m.Target))
            .ToList();

        Assert.Equal(reunis.Count, reunis.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>L'outil de flux nomme des nœuds qui existent, avec les types qui vont bien.</summary>
    /// <remarks>
    /// La cible est reconstruite exactement comme le panneau la produira : chaque champ remplacé par
    /// sa valeur par défaut, le jeton <c>{tools}</c> résolu. Ce qui est éprouvé n'est donc pas une
    /// cible écrite pour l'occasion, mais celle qu'un utilisateur obtiendra en ouvrant le panneau et
    /// en cliquant sans rien changer.
    /// </remarks>
    [Fact]
    public void TheFlowToolMatchesTheFlowItShips()
    {
        var racine = Racine();
        var outil = PluginCatalog.Scan(Path.Combine(racine, "Plugins")).Loaded
            .FirstOrDefault(m => m.Id == "flux-image-video");

        Assert.True(outil is not null, "l'outil 'flux-image-video' n'a pas été chargé");

        var cible = outil!.Content.First(c => c.Kind == "action").Target;

        foreach (var champ in outil.Content.Where(c => c.Id.Length > 0))
        {
            // Un champ « file » n'a pas de valeur par défaut : l'utilisateur la désigne. N'importe
            // quel nom fait l'affaire ici, puisque c'est le nœud visé qu'on éprouve, pas le fichier.
            var valeur = champ.Value.Length > 0 ? champ.Value : "image.png";

            cible = cible.Replace("{" + champ.Id + "}", valeur, StringComparison.Ordinal);
        }

        cible = cible.Replace(
            "{tools}", Path.Combine(racine, "Outils"), StringComparison.OrdinalIgnoreCase);

        var lecture = FluxTravail.Lire(cible);

        Assert.Equal("", lecture.Faute);
        Assert.True(File.Exists(lecture.Chemin), $"flux absent : {lecture.Chemin}");

        var pose = FluxTravail.Appliquer(File.ReadAllText(lecture.Chemin), lecture.Reglages, out var faute);

        Assert.Null(faute);

        using var document = JsonDocument.Parse(pose);

        // Les trois types que le manifeste traverse : un compte, un nom, et une cadence qui atterrit
        // dans deux nœuds différents.
        Assert.Equal("20", Entree(document, "6", "steps").GetRawText());
        Assert.Equal("6", Entree(document, "4", "fps").GetRawText());
        Assert.Equal("animation", Entree(document, "9", "filename_prefix").GetString());
    }

    private static JsonElement Entree(JsonDocument document, string noeud, string entree)
        => document.RootElement.GetProperty(noeud).GetProperty("inputs").GetProperty(entree);

    /// <summary>
    /// Aucun outil ne laisse un flux geler un nom de fichier.
    /// </summary>
    /// <remarks>
    /// <b>C'est la règle de maintenance, rendue impossible à oublier.</b> Un flux qui nomme
    /// <c>wan2.1_i2v_480p_14B.safetensors</c> fonctionne jusqu'au jour où ce fichier n'est plus là,
    /// et ce jour-là il ne se plaint pas : il échoue au premier clic, des mois plus tard, sur un nom
    /// que personne ne reconnaît. Deux flux de cette machine ont pourri exactement ainsi, en
    /// silence.
    ///
    /// <para>
    /// La couverture ne suffit pas : le champ qui recouvre doit lui-même tirer ses valeurs de la
    /// machine — un <c>from</c> — ou être un fichier que l'utilisateur désigne. Un <c>choice</c> aux
    /// options écrites à la main ne ferait que déplacer le gel du flux vers le manifeste.
    /// </para>
    ///
    /// <para>
    /// L'épreuve échoue en disant quoi écrire, et non seulement que quelque chose manque : c'est la
    /// différence entre un garde-fou et une punition.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoShippedFlowFreezesAFileName()
    {
        var racine = Racine();
        var manques = new List<string>();

        foreach (var outil in PluginCatalog.Scan(Path.Combine(racine, "Plugins")).Loaded)
        {
            manques.AddRange(FluxGel
                .Examiner(outil, cible => cible.Replace(
                    "{tools}", Path.Combine(racine, "Outils"), StringComparison.OrdinalIgnoreCase))
                .Select(g => g.ToString()));
        }

        Assert.True(manques.Count == 0, string.Join("\n", manques));
    }
}
