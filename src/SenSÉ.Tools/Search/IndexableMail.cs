using System.Text;
using System.Text.RegularExpressions;

namespace SenSÉ.Tools.Search;

/// <summary>
/// What is worth keeping from a message, and what is only noise.
/// </summary>
public static partial class IndexableMail
{
    /// <summary>Beyond this a message is quoted history, not a message.</summary>
    public const int MaxCharacters = 20_000;

    /// <summary>
    /// Turns the readable part of a message into plain text.
    /// </summary>
    /// <remarks>
    /// Most mail arrives as HTML, and indexing it raw fills the index with tag names and inline
    /// styles — a query for <c>span</c> would match half the mailbox while the words the sender
    /// actually wrote sit buried among them. Styles and scripts are dropped whole rather than
    /// stripped of tags, because their contents are not text anybody wrote.
    /// </remarks>
    public static string StripMarkup(string body)
    {
        if (body.Length == 0)
        {
            return "";
        }

        var looksLikeHtml = body.Contains("</", StringComparison.OrdinalIgnoreCase)
            || body.Contains("<br", StringComparison.OrdinalIgnoreCase);

        var text = looksLikeHtml
            ? TagPattern().Replace(StyleAndScriptPattern().Replace(body, " "), " ")
            : body;

        text = Entities(text);

        return Collapse(text);
    }

    /// <summary>The handful of entities that appear in ordinary prose.</summary>
    private static string Entities(string text)
        => text.Contains('&')
            ? text.Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
                .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
                .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
                .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase)
                .Replace("&#39;", "'", StringComparison.Ordinal)
            : text;

    /// <summary>Squeezes the whitespace a stripped message is mostly made of.</summary>
    private static string Collapse(string text)
    {
        var result = new StringBuilder(Math.Min(text.Length, MaxCharacters));
        var lastWasSpace = false;

        foreach (var character in text)
        {
            var isSpace = char.IsWhiteSpace(character);

            if (isSpace)
            {
                if (!lastWasSpace && result.Length > 0)
                {
                    result.Append(' ');
                }
            }
            else
            {
                result.Append(character);
            }

            lastWasSpace = isSpace;

            if (result.Length >= MaxCharacters)
            {
                break;
            }
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// Replaces web addresses with a marker, for reading only.
    /// </summary>
    /// <remarks>
    /// A tracking address is four hundred characters of base-64-looking noise, and a newsletter
    /// carries dozens. Measured on a real mailbox, seventy-seven messages had their prose buried
    /// under them — the text was decoded correctly and still unreadable.
    ///
    /// <para>
    /// <b>Never applied to what is indexed.</b> The address is real content: somebody searching for
    /// a domain, a ticket number in a link or the name of a service must still find the message.
    /// This runs when the message is shown, not when it is stored, and that separation is the whole
    /// point — the searchable form and the readable form are two different things.
    /// </para>
    ///
    /// <para>
    /// Runs of markers collapse into one. A footer of fifteen links otherwise becomes fifteen
    /// <c>[lien]</c> in a row, which is exactly as unreadable as what it replaced.
    /// </para>
    /// </remarks>
    public static string CollapseLinks(string text)
        => text.Length == 0
            ? text
            : RepeatedMarkerPattern().Replace(LinkPattern().Replace(text, Marker), Marker);

    /// <summary>What stands in for an address.</summary>
    public const string Marker = "[lien]";

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"(?:\[lien\][\s,;.·|-]*){2,}")]
    private static partial Regex RepeatedMarkerPattern();

    [GeneratedRegex("<(style|script)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StyleAndScriptPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}
