using System.Text;
using System.Text.Json;

// Downloads Iconify icon sets and writes each one as individual SVG files in its own folder.
//
// Written in .NET rather than PowerShell because Windows PowerShell 5.1 cannot parse some of these
// files at all: ConvertFrom-Json builds a PSObject, and a few sets carry icon names that are not
// valid PSObject property names, which fails the whole document. System.Text.Json reads them as
// plain dictionary keys and does not care.
//
// Only the sets named on the command line are fetched. Iconify aggregates 231 sets under 19
// licences, some non-commercial, so there is no "fetch everything" mode here on purpose.

if (args.Length < 2)
{
    Console.WriteLine("Usage: IconFetch <destination> <set> [<set> ...]");
    return 1;
}

var destination = args[0];
var sets = args.Skip(1).ToArray();

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("SteamXBox-IconFetch/1.0");

var utf8 = new UTF8Encoding(false);
var totals = new List<(string Set, int Icons, string License, double Megabytes)>();

foreach (var prefix in sets)
{
    Console.WriteLine($"=== {prefix} ===");

    JsonDocument document;
    try
    {
        var url = $"https://raw.githubusercontent.com/iconify/icon-sets/master/json/{prefix}.json";
        await using var stream = await http.GetStreamAsync(url);
        document = await JsonDocument.ParseAsync(stream);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"  download failed: {exception.GetType().Name}: {exception.Message}");
        continue;
    }

    using (document)
    {
        var root = document.RootElement;

        if (!root.TryGetProperty("icons", out var icons))
        {
            Console.WriteLine("  no 'icons' section; skipped.");
            continue;
        }

        // Not every set uses whole numbers: some carry fractional canvases, so these are read as
        // doubles. GetInt32 threw on the first such set and aborted the whole run.
        var defaultWidth = Dimension(root, "width", 16);
        var defaultHeight = Dimension(root, "height", 16);

        var directory = Path.Combine(destination, prefix);
        Directory.CreateDirectory(directory);

        var written = 0;
        long bytes = 0;

        foreach (var icon in icons.EnumerateObject())
        {
            var name = SafeName(icon.Name);
            if (name is null)
            {
                continue;
            }

            if (!icon.Value.TryGetProperty("body", out var bodyElement))
            {
                continue;
            }

            var body = bodyElement.GetString();
            if (string.IsNullOrEmpty(body))
            {
                continue;
            }

            var width = Dimension(icon.Value, "width", defaultWidth);
            var height = Dimension(icon.Value, "height", defaultHeight);

            // Invariant culture: a French machine would otherwise write "24,5" and produce SVG that
            // no renderer accepts.
            var ws = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var hs = height.ToString(System.Globalization.CultureInfo.InvariantCulture);

            var svg = $"""<svg xmlns="http://www.w3.org/2000/svg" width="{ws}" height="{hs}" viewBox="0 0 {ws} {hs}">{body}</svg>""";

            var path = Path.Combine(directory, name + ".svg");
            File.WriteAllText(path, svg, utf8);
            written++;
            bytes += svg.Length;
        }

        var info = root.TryGetProperty("info", out var i) ? i : default;
        var license = Read(info, "license", "title") ?? "unknown";

        // The licence travels with the icons: separated from it, they are legally unusable.
        var notice = string.Join("\r\n",
        [
            $"{Read(info, "name") ?? prefix} - Iconify icon set",
            "",
            $"Prefix   : {prefix}",
            $"Icons    : {written}",
            $"Author   : {Read(info, "author", "name") ?? "unknown"}",
            $"Source   : {Read(info, "author", "url") ?? "unknown"}",
            $"License  : {license} ({Read(info, "license", "spdx") ?? "-"})",
            $"Text     : {Read(info, "license", "url") ?? "-"}",
            "",
            "Retrieved from https://github.com/iconify/icon-sets",
        ]);

        File.WriteAllText(Path.Combine(directory, "LICENSE.txt"), notice, utf8);

        var megabytes = Math.Round(bytes / 1024.0 / 1024.0, 1);
        Console.WriteLine($"  {written} icons, {megabytes} MB, {license}");
        totals.Add((prefix, written, license, megabytes));
    }
}

Console.WriteLine();
Console.WriteLine($"{"set",-20} {"icons",8} {"MB",6}  license");
foreach (var (set, count, license, megabytes) in totals)
{
    Console.WriteLine($"{set,-20} {count,8} {megabytes,6}  {license}");
}
Console.WriteLine();
Console.WriteLine($"{totals.Count} sets, {totals.Sum(t => t.Icons)} icons, {Math.Round(totals.Sum(t => t.Megabytes), 1)} MB");

return 0;

/// <summary>Reads a canvas dimension, tolerating fractional and malformed values.</summary>
static double Dimension(JsonElement element, string name, double fallback)
{
    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
    {
        return fallback;
    }

    return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && number > 0
        ? number
        : fallback;
}

// Icon names come from a third-party repository: they must never escape the destination folder.
static string? SafeName(string name)
{
    var builder = new StringBuilder(name.Length);
    foreach (var character in name)
    {
        builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
            ? character
            : '_');
    }

    var safe = builder.ToString();
    return safe is "" or "." or ".." ? null : safe;
}

static string? Read(JsonElement element, params string[] path)
{
    var current = element;
    foreach (var key in path)
    {
        if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(key, out current))
        {
            return null;
        }
    }

    return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
}
