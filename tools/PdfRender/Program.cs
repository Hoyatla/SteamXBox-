using System.IO;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

// Renders PDF pages to PNG using the renderer built into Windows.
//
// Written because this machine has no poppler, no Ghostscript and no ImageMagick, and the document
// in question is a screenshot export with no text layer: pdftotext returns zero characters, so the
// only way to read it is to look at it.

if (args.Length < 2)
{
    Console.WriteLine("Usage: PdfRender <file.pdf> <output-folder> [firstPage] [lastPage] [width]");
    return 1;
}

var source = args[0];
var destination = args[1];
var first = args.Length > 2 && int.TryParse(args[2], out var f) ? Math.Max(1, f) : 1;
var last = args.Length > 3 && int.TryParse(args[3], out var l) ? l : int.MaxValue;

// Wide enough to read interface text in a screenshot, small enough not to produce huge files.
var width = args.Length > 4 && uint.TryParse(args[4], out var w) ? w : 1400u;

Directory.CreateDirectory(destination);

var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(source));
var document = await PdfDocument.LoadFromFileAsync(file);

Console.WriteLine($"{document.PageCount} pages");
last = Math.Min(last, (int)document.PageCount);

for (var index = first; index <= last; index++)
{
    using var page = document.GetPage((uint)(index - 1));
    var target = Path.Combine(destination, $"page-{index:D2}.png");

    using var stream = new InMemoryRandomAccessStream();
    await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = width });

    // Through a managed stream rather than an IBuffer: the AsBuffer extension needs the WinRT
    // interop package, and copying is simpler than adding a dependency for one call.
    stream.Seek(0);
    using var managed = stream.AsStreamForRead();
    using var output = File.Create(target);
    await managed.CopyToAsync(output);

    Console.WriteLine($"  {Path.GetFileName(target)}  {output.Length / 1024} KB");
}

return 0;
