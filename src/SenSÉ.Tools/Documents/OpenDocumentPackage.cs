using System.IO.Compression;
using System.Text;

namespace SenSÉ.Tools.Documents;

/// <summary>A picture inside an OpenDocument package.</summary>
/// <param name="Path">Where it sits in the archive, and what the content refers to it by.</param>
internal readonly record struct PackedPicture(string Path, byte[] Bytes, string MediaType);

/// <summary>
/// The envelope every OpenDocument file comes in.
/// </summary>
/// <remarks>
/// A presentation and a text document differ entirely in their content and not at all in their
/// packaging: the same archive, the same four parts, the same two rules about how they go in. Both
/// of those rules were learned the hard way, so they live in one place rather than in each writer.
///
/// <para>
/// <b>The mimetype is written first and stored uncompressed</b>, because a reader identifies the
/// file by finding those bytes at a fixed offset in the archive.
/// </para>
///
/// <para>
/// <b>Every part is declared in the manifest</b>, pictures included. A file present in the archive
/// but absent from the manifest is not read — which shows up as a document whose images are all
/// blank, with nothing anywhere saying why.
/// </para>
/// </remarks>
internal static class OpenDocumentPackage
{
    internal const string Text = "application/vnd.oasis.opendocument.text";

    internal const string Presentation = "application/vnd.oasis.opendocument.presentation";

    /// <summary>Writes the archive.</summary>
    internal static void Write(
        string output,
        string mimetype,
        string content,
        string styles,
        IReadOnlyList<PackedPicture> pictures)
    {
        using var file = new FileStream(output, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);

        var first = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);

        using (var writer = new StreamWriter(first.Open(), new UTF8Encoding(false)))
        {
            writer.Write(mimetype);
        }

        foreach (var picture in pictures)
        {
            // Already compressed formats, both of them. Deflating a JPEG again costs time and adds
            // size.
            var entry = zip.CreateEntry(picture.Path, CompressionLevel.NoCompression);

            using var stream = entry.Open();
            stream.Write(picture.Bytes);
        }

        Add(zip, "content.xml", content);
        Add(zip, "styles.xml", styles);
        Add(zip, "meta.xml", Meta());
        Add(zip, "META-INF/manifest.xml", Manifest(mimetype, pictures));
    }

    private static void Add(ZipArchive zip, string path, string text)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);

        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string Meta() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-meta
          xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
          xmlns:meta="urn:oasis:names:tc:opendocument:xmlns:meta:1.0"
          office:version="1.2">
          <office:meta>
            <meta:generator>SenSÉ</meta:generator>
          </office:meta>
        </office:document-meta>
        """;

    private static string Manifest(string mimetype, IReadOnlyList<PackedPicture> pictures)
    {
        var xml = new StringBuilder();

        xml.Append("""
                   <?xml version="1.0" encoding="UTF-8"?>
                   <manifest:manifest
                     xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0"
                     manifest:version="1.2">

                   """);

        xml.Append("  <manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" ")
            .Append("manifest:media-type=\"").Append(mimetype).Append("\"/>\n");

        foreach (var part in new[] { "content.xml", "styles.xml", "meta.xml" })
        {
            xml.Append("  <manifest:file-entry manifest:full-path=\"").Append(part)
                .Append("\" manifest:media-type=\"text/xml\"/>\n");
        }

        foreach (var picture in pictures)
        {
            xml.Append("  <manifest:file-entry manifest:full-path=\"").Append(picture.Path)
                .Append("\" manifest:media-type=\"").Append(picture.MediaType).Append("\"/>\n");
        }

        xml.Append("</manifest:manifest>");

        return xml.ToString();
    }
}
