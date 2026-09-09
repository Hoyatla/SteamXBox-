using System.Text;
using System.Text.RegularExpressions;

namespace SenSÉ.Tools.Assistant;

/// <summary>
/// Décide quelle voie doit traiter une demande : par des règles quand c'est gratuit, par le
/// modèle quand il faut juger.
/// </summary>
/// <remarks>
/// <b>Ce qui se décide gratuitement ne doit pas coûter une génération.</b> Un fichier son déposé va
/// à la transcription, « écris-moi une fonction » au codage : ces cas se tranchent sur la demande
/// elle-même, sans réveiller personne. Le modèle ne voit que le reste.
///
/// <para>
/// <b>Le peu qui reste coûte 232 ms, et c'est mesuré.</b> Banc du 9 septembre 2026, huit demandes
/// vers cinq voies : Qwen 2.5 Coder 3B répond juste 7 fois sur 8 en 232 ms, contre 848 ms pour le
/// 9B à égalité de justesse, et 284 ms pour Ling 3.0 tiny qui n'en réussit que 4. L'aiguillage
/// n'est pas cher à cause du nombre de paramètres — il l'est à cause de la longueur de l'invite,
/// d'où celle-ci, volontairement minuscule.
/// </para>
///
/// <para>
/// <b>Un biais connu, corrigé dans l'invite.</b> L'unique erreur du banc était « Explique-moi la
/// différence entre RAM et VRAM » envoyée au codage : pour un modèle de code, une question
/// technique ressemble à du code. La consigne dit donc explicitement qu'une question à laquelle on
/// répond <i>en prose</i> va au dialogue, même technique.
/// </para>
///
/// <para>
/// <b>Ce que l'aiguilleur ne fait jamais</b> : proposer une voie absente. Il ne connaît que ce que
/// <see cref="Moteurs.Voies"/> déclare, et une réponse hors de cette liste est refusée plutôt que
/// suivie. C'est la règle déjà tenue pour les recherches non configurées et le mode interactif —
/// un modèle ne s'entête pas sur ce qu'il ignore.
/// </para>
/// </remarks>
public static class Aiguilleur
{
    /// <summary>La voie par défaut : celle qui sait tenir une conversation.</summary>
    public const string Defaut = "dialogue";

    /// <summary>Ce que l'aiguilleur a décidé, et par quel moyen.</summary>
    /// <param name="Voie">La voie retenue.</param>
    /// <param name="Raison">Ce qui a tranché : une règle nommée, le modèle, ou le repli.</param>
    public readonly record struct Choix(string Voie, string Raison)
    {
        public override string ToString() => $"{Voie} ({Raison})";
    }

    /// <summary>
    /// Décide, sans jamais échouer.
    /// </summary>
    /// <param name="demande">Ce que l'utilisateur a écrit.</param>
    /// <param name="voies">Les voies réellement servies. Une voie absente n'est jamais rendue.</param>
    /// <param name="demander">
    /// Comment interroger l'aiguilleur : reçoit la consigne et la demande, rend sa réponse brute.
    /// Injecté pour que les épreuves n'aient besoin d'aucun serveur.
    /// </param>
    /// <remarks>
    /// Rend toujours quelque chose. Un aiguilleur qui refuse de trancher rendrait la main à
    /// l'utilisateur pour une question qu'il n'a pas posée ; le repli sur le dialogue est le
    /// comportement d'avant l'aiguillage, donc jamais une régression.
    /// </remarks>
    public static Choix Decider(
        string demande,
        IReadOnlyList<string> voies,
        Func<string, string, string?>? demander = null,
        Action<string>? journal = null)
    {
        var propre = (demande ?? "").Trim();

        if (propre.Length == 0 || voies.Count == 0)
        {
            return Retenir(new Choix(Defaut, "demande vide ou aucune voie"), journal);
        }

        if (ParLaRegle(propre, voies) is { } tranche)
        {
            return Retenir(tranche, journal);
        }

        if (demander is null)
        {
            return Retenir(new Choix(Defaut, "aucun aiguilleur disponible"), journal);
        }

        var dit = "";

        try
        {
            dit = demander(Consigne(voies), propre) ?? "";
        }
        catch (Exception exception)
        {
            journal?.Invoke($"aiguillage : {exception.GetType().Name}, repli sur {Defaut}.");

            return Retenir(new Choix(Defaut, "l'aiguilleur n'a pas répondu"), journal);
        }

        return Retenir(Lire(dit, voies), journal);
    }

    /// <summary>Ce qui se décide sans modèle, ou null.</summary>
    /// <remarks>
    /// Volontairement court. Chaque règle ajoutée est une chance de se tromper avec certitude —
    /// et le modèle, lui, tranche pour 232 ms. N'entrent ici que les cas où se tromper est
    /// impossible : une extension de fichier, et une poignée de tournures sans ambiguïté.
    /// </remarks>
    private static Choix? ParLaRegle(string demande, IReadOnlyList<string> voies)
    {
        // Un fichier son designe sa voie sans discussion.
        if (Sonore.IsMatch(demande) && voies.Contains("transcription", StringComparer.OrdinalIgnoreCase))
        {
            return new Choix("transcription", "un fichier son est nommé");
        }

        // Ecrire du code se demande explicitement, et c'est la seule tournure ou « ecris » ne
        // signifie pas rediger.
        if (Coder.IsMatch(demande) && voies.Contains("codage", StringComparer.OrdinalIgnoreCase))
        {
            return new Choix("codage", "du code est demandé explicitement");
        }

        return null;
    }

    /// <summary>La consigne de l'aiguilleur : la plus courte qui tienne.</summary>
    /// <remarks>
    /// Les trois exemples sont ceux du banc, et ils ne sont pas décoratifs : sans eux, le même
    /// modèle passait de 7/8 à 3/6 et répondait parfois à la demande au lieu de l'aiguiller.
    /// </remarks>
    private static string Consigne(IReadOnlyList<string> voies)
    {
        var texte = new StringBuilder();

        texte.AppendLine(
            "Tu es un aiguilleur. Tu ne réponds JAMAIS à la demande : tu nommes seulement la voie "
            + "qui doit la traiter.");

        texte.AppendLine("Voies :");

        foreach (var voie in voies)
        {
            texte.Append("- ").Append(voie);

            if (Glose.TryGetValue(voie, out var quoi))
            {
                texte.Append(" : ").Append(quoi);
            }

            texte.AppendLine();
        }

        texte.AppendLine("Ta réponse est UN SEUL MOT choisi dans cette liste. Rien d'autre.");

        return texte.ToString();
    }

    /// <summary>Ce que chaque voie fait, en une ligne.</summary>
    /// <remarks>
    /// <b>Un nom de voie sans définition se devine, et il se devine mal.</b> La liste était nue —
    /// « dialogue, codage, image, transcription, video » — et le 9 septembre 2026 une demande
    /// d'actualités est partie deux fois de suite à <c>transcription</c>. Le mot, seul, peut se lire
    /// comme « retranscrire une information » ; il ne désigne en réalité que le passage d'un fichier
    /// AUDIO au texte, ce que rien dans la consigne ne disait.
    ///
    /// <para>
    /// Le coût est d'une trentaine de jetons sur une invite qui en faisait 161. Ce qu'il achète est
    /// la disparition d'une classe entière d'erreurs : celles où le modèle ne se trompe pas de
    /// jugement mais de vocabulaire.
    /// </para>
    ///
    /// <para>
    /// Par rôle et non par identifiant : remplacer Whisper par un autre transcripteur ne change pas
    /// ce que « transcription » veut dire. Une voie absente d'ici garde son nom nu — c'est le cas
    /// d'un rôle ajouté après coup, et il vaut mieux une ligne sans glose qu'une glose inventée.
    /// </para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Glose =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codage"] =
                "écrire ou corriger du CODE SOURCE. Pas les questions sur l'informatique, "
                + "qui vont au dialogue.",
            ["dialogue"] =
                "converser, expliquer, résumer un texte ou un document, chercher une information "
                + "ou une actualité. Tout ce qui se répond en prose. C'est la voie par défaut : "
                + "dans le doute, c'est elle.",
            ["image"] = "FABRIQUER une image à partir d'une description.",
            ["transcription"] =
                "convertir un fichier SON (wav, mp3, enregistrement, dictée) en texte. JAMAIS un "
                + "document, jamais un PDF, jamais une page web.",
            ["video"] = "FABRIQUER une vidéo à partir d'une description.",
        };

    /// <summary>Les exemples que la consigne accompagne, pour l'appelant qui construit le dialogue.</summary>
    /// <remarks>
    /// Rendus à part plutôt qu'inclus dans la consigne : le dialecte OpenAI veut de vrais tours
    /// utilisateur/assistant, et un modèle suit bien mieux un échange qu'une liste recopiée dans
    /// une consigne.
    /// </remarks>
    public static IReadOnlyList<(string Demande, string Voie)> Exemples { get; } =
    [
        ("Écris un script bash qui renomme des fichiers.", "codage"),
        ("Qui a peint la Joconde ?", "dialogue"),
        ("Génère une photo de montagne au coucher du soleil.", "image"),
    ];

    /// <summary>Ce que le modèle a répondu, ramené à une voie servie.</summary>
    /// <remarks>
    /// Un mot hors liste est refusé, pas rattrapé au plus proche. Un aiguilleur qui devine ce
    /// qu'on a voulu dire finit par envoyer une vidéo au transcripteur, et l'erreur ne se voit
    /// qu'après plusieurs minutes de travail.
    /// </remarks>
    private static Choix Lire(string dit, IReadOnlyList<string> voies)
    {
        var mot = dit.Trim().Trim('.', ',', ':', ';', '!', '"', '\'', ' ', '\n', '\r').ToLowerInvariant();

        foreach (var voie in voies)
        {
            if (string.Equals(mot, voie, StringComparison.OrdinalIgnoreCase))
            {
                return new Choix(voie, "l'aiguilleur a tranché");
            }
        }

        return new Choix(Defaut, $"réponse hors liste ({Ecourter(mot)})");
    }

    private static Choix Retenir(Choix choix, Action<string>? journal)
    {
        journal?.Invoke($"aiguillage : {choix.Voie} — {choix.Raison}.");

        return choix;
    }

    private static string Ecourter(string mot)
        => mot.Length <= 24 ? mot : mot[..24] + "…";

    private static readonly Regex Sonore = new(
        @"\.(wav|mp3|m4a|ogg|flac|aac|wma|opus)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Coder = new(
        @"\b(script|fonction|classe|programme|requête sql|regex)\b|\bdu code\b|\bcompile[rz]?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
