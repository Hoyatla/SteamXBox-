namespace SenSÉ.Tools.Search;

/// <summary>
/// Le nom sous lequel le produit se présente aux serveurs qu'il interroge.
/// </summary>
/// <remarks>
/// <b>Il est en ASCII, et ce n'est pas un détail de style.</b> Une valeur d'en-tête HTTP ne peut
/// pas porter de caractère non-ASCII : .NET le vérifie à l'ajout et lève une
/// <see cref="FormatException"/>. Quand cet ajout se fait dans un champ statique — le cas ordinaire
/// pour un <c>HttpClient</c> partagé — l'exception ressort en
/// <c>TypeInitializationException</c> au premier usage de la classe, un message qui ne dit rien de
/// la cause.
///
/// <para>
/// <b>Ce défaut a dormi longtemps.</b> <c>SearxngClient</c> se présentait comme « SenSÉ », avec son
/// accent. La classe n'était jamais touchée tant qu'aucune instance de recherche n'était
/// configurée — donc jamais, sur aucune machine. Le jour où une instance a été installée, la
/// première recherche a rendu « The type initializer for 'SenSÉ.Desktop.Search.SearxngClient' threw
/// an exception », et rien dans cette phrase ne menait à un accent dans un en-tête.
/// </para>
///
/// <para>
/// Déclaré ici plutôt que chez chaque appelant : le produit se présente d'une seule façon, et une
/// épreuve peut atteindre ce point-ci. C'est ce qui manquait pour que le défaut soit attrapé avant
/// l'utilisateur.
/// </para>
/// </remarks>
public static class AgentHttp
{
    /// <summary>Ce que le produit met dans <c>User-Agent</c>.</summary>
    /// <remarks>
    /// Nommé honnêtement : certains serveurs refusent une requête sans agent, et qui lit ses
    /// propres journaux doit pouvoir voir ce que tournent ses utilisateurs. Sans accent, parce
    /// qu'un en-tête HTTP n'en accepte pas.
    /// </remarks>
    public const string Nom = "SenSE";

    /// <summary>
    /// L'agent d'un navigateur, pour lire une page publique comme un lecteur la lirait.
    /// </summary>
    /// <remarks>
    /// Employé par la lecture de pages web, et pas par l'interrogation d'une instance : un agent
    /// inconnu se fait servir une coquille par la moitié des sites de presse, ce qui déclencherait
    /// un repli sur le navigateur à chaque fois. Une instance, elle, n'a pas ce réflexe et mérite
    /// d'être renseignée sur ce qui l'interroge.
    /// </remarks>
    public const string Navigateur =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/131.0.0.0 Safari/537.36";
}
