using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SenSÉ.Tools.Indexing;

namespace SenSÉ.Indexer;

/// <summary>
/// Walks a folder, extracts what text it can, and pushes it into a Meilisearch index.
/// </summary>
/// <remarks>
/// Run by whoever administers the corpus, on the machine that sees the share, as often as they
/// choose. It prints what it did and returns a code, so a scheduled task can tell whether it worked.
///
/// <code>
/// SenSÉ.Indexer --root \\serveur\documents --url https://corpus.interne --index documents --key CLE
/// </code>
///
/// <para>
/// Documents are keyed by a hash of their path, so running it again updates rather than duplicates.
/// Nothing is deleted: a file removed from the share stays in the index until somebody clears it,
/// which is deliberate — an indexer that deletes on a run that failed halfway would empty a corpus
/// because a network share blinked.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Documents per request. Enough to be fast, small enough not to time out.</summary>
    private const int BatchSize = 100;

    public static async Task<int> Main(string[] args)
    {
        // Mode diagnostic : il n'indexe rien et n'envoie rien, il decrit un PDF tel que PdfPig le
        // voit. Place avant tout le reste, parce qu'il ne demande ni racine, ni adresse, ni cle.
        if (Argument(args, "--diag-pdf") is { } aDecrire)
        {
            return PdfDiagnostic.Run(aDecrire);
        }

        var root = Argument(args, "--root");
        var url = Argument(args, "--url");
        var index = Argument(args, "--index") ?? "documents";
        var key = Argument(args, "--key") ?? "";

        // Reads and reports without sending anything. An administrator should be able to see what a
        // share would put into the index before it goes in, and it is the only way to tell an
        // extraction problem from a network one.
        var dryRun = args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));

        // Demandee explicitement, jamais deduite. Voir le bloc qui la construit plus bas : c'est le
        // temps d'indexation du partage qui est en jeu, pas la qualite d'un resultat.
        var ocr = args.Any(a => a.Equals("--ocr", StringComparison.OrdinalIgnoreCase));

        if (root is null || (url is null && !dryRun))
        {
            Console.Error.WriteLine(
                "Usage: SenSÉ.Indexer --root <dossier> [--url <instance>] "
                + "[--index <nom>] [--key <cle>] [--dry-run] [--ocr]");

            return 2;
        }

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"Le dossier « {root} » est introuvable.");
            return 2;
        }

        // Cheapest first, and LibreOffice last: it starts a process per file, so anything another
        // extractor can read must never reach it.
        using var libreOffice = new LibreOfficeTextExtractor();

        // La reconnaissance de caracteres : eteinte par defaut, et volontairement. Un partage plein
        // de scans passe d'une heure d'indexation a une nuit — c'est une decision d'exploitation,
        // pas un reglage qui s'active tout seul.
        //
        // Placee AVANT PdfTextExtractor : elle lit d'abord le texte de chaque page comme lui, et ne
        // dessine que les pages qui n'en ont pas. Derriere lui, elle ne verrait jamais un seul PDF.
        using var renderer = new PdfToPpmPageRenderer();
        var scanned = new ScannedPdfTextExtractor(renderer, new TesseractTextRecognizer());

        if (ocr)
        {
            Console.WriteLine(scanned.IsAvailable
                ? $"Reconnaissance de caracteres active : {scanned.Describe()}"
                : $"Reconnaissance de caracteres demandee mais indisponible : {scanned.Describe()}");
        }

        var extractors = ocr && scanned.IsAvailable
            ? new ITextExtractor[]
            {
                new PlainTextExtractor(),
                scanned,
                new PdfTextExtractor(),
                new OfficeTextExtractor(),
                libreOffice,
            }
            : new ITextExtractor[]
            {
                new PlainTextExtractor(),
                new PdfTextExtractor(),
                new OfficeTextExtractor(),
                libreOffice,
            };

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        if (key.Length > 0)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        // Empty in a dry run, where no address was required and none is used.
        var endpoint = url is null
            ? ""
            : $"{url.TrimEnd('/')}/indexes/{Uri.EscapeDataString(index)}/documents";

        var batch = new List<IndexableDocument>(BatchSize);
        var pushed = 0;
        var skipped = 0;

        foreach (var path in Walk(root))
        {
            var extension = Path.GetExtension(path);
            var extractor = extractors.FirstOrDefault(e => e.Handles(extension));

            if (extractor is null)
            {
                skipped++;
                continue;
            }

            var content = extractor.Extract(path);

            if (content.Length == 0)
            {
                skipped++;
                continue;
            }

            batch.Add(IndexableDocument.From(path, content));

            if (dryRun)
            {
                Console.WriteLine($"  {content.Length,8} caractères  {path}");
                pushed++;
                batch.Clear();
                continue;
            }

            if (batch.Count >= BatchSize)
            {
                pushed += await PushAsync(client, endpoint, batch).ConfigureAwait(false);
                batch.Clear();
            }
        }

        if (batch.Count > 0 && !dryRun)
        {
            pushed += await PushAsync(client, endpoint, batch).ConfigureAwait(false);
        }

        Console.WriteLine(dryRun
            ? $"{pushed} document(s) seraient indexés, {skipped} ignoré(s). Rien n'a été envoyé."
            : $"{pushed} document(s) envoyé(s), {skipped} ignoré(s).");

        // A run that pushed nothing is a failure worth a non-zero code: a scheduled task must be
        // able to notice that the share moved, the key expired, or nothing matched.
        return pushed > 0 ? 0 : 1;
    }

    /// <summary>Every file below the root, skipping the folders that never hold documents.</summary>
    /// <remarks>
    /// Walked by hand rather than with <c>AllDirectories</c>, for the reason the launcher's own
    /// index learned the hard way: that option abandons the whole walk at the first folder it cannot
    /// open, and a share always has one.
    /// </remarks>
    private static IEnumerable<string> Walk(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var directory = pending.Dequeue();

            string[] files;

            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Ignoré : {directory} ({exception.Message})");
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] children;

            try
            {
                children = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                if (IndexableFiles.ShouldDescend(Path.GetFileName(child)))
                {
                    pending.Enqueue(child);
                }
            }
        }
    }

    /// <summary>Sends one batch, and says what happened rather than throwing.</summary>
    private static async Task<int> PushAsync(
        HttpClient client, string endpoint, List<IndexableDocument> batch)
    {
        try
        {
            var body = JsonSerializer.Serialize(batch);

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  {batch.Count} document(s)…");
                return batch.Count;
            }

            Console.Error.WriteLine(
                $"L'instance a refusé un lot ({(int)response.StatusCode}) : "
                + await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            return 0;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // One batch lost, the walk continues. A share of forty thousand files must not be
            // abandoned because the instance hiccuped once.
            Console.Error.WriteLine($"Lot perdu : {exception.Message}");
            return 0;
        }
    }

    private static string? Argument(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
