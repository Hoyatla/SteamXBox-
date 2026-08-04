using SteamXBox.Tools.Clipboard;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset When = new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);

    private static ClipboardEntry Text(string text) => new(ClipboardKind.Text, text, null, When);

    private static ClipboardEntry Image(string label) => new(ClipboardKind.Image, label, new object(), When);

    [Fact]
    public void NewestFirst()
    {
        var history = new ClipboardHistory();
        history.Add(Text("un"));
        history.Add(Text("deux"));

        Assert.Equal("deux", history.Entries[0].Text);
        Assert.Equal("un", history.Entries[1].Text);
    }

    // Applications re-copy the same thing constantly. Without this the history fills with duplicates
    // of whatever was last touched and the older entries fall off the end.
    [Fact]
    public void RefusesARepeatOfTheTopEntry()
    {
        var history = new ClipboardHistory();
        Assert.True(history.Add(Text("même")));
        Assert.False(history.Add(Text("même")));
        Assert.Single(history.Entries);
    }

    // A, B, A is three real events, and the user expects A back at the top rather than left in
    // place — so the check is against the top entry only, not the whole list.
    [Fact]
    public void ARepeatThatIsNotAtTheTopIsARealEvent()
    {
        var history = new ClipboardHistory();
        history.Add(Text("A"));
        history.Add(Text("B"));

        Assert.True(history.Add(Text("A")));
        Assert.Equal(3, history.Entries.Count);
        Assert.Equal("A", history.Entries[0].Text);
    }

    [Fact]
    public void ComparisonIsCaseAndWhitespaceSensitive()
    {
        var history = new ClipboardHistory();
        history.Add(Text("Mot"));

        Assert.True(history.Add(Text("mot")));
        Assert.True(history.Add(Text("mot ")));
    }

    // Comparing images would mean comparing pixels, which this library cannot do: two screenshots a
    // second apart are two events even when identical.
    [Fact]
    public void ImagesAreNeverTreatedAsRepeats()
    {
        var history = new ClipboardHistory();
        history.Add(Image("Image 100×100"));

        Assert.True(history.Add(Image("Image 100×100")));
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public void DropsTheOldestBeyondCapacity()
    {
        var history = new ClipboardHistory();
        for (var i = 0; i <= ClipboardHistory.Capacity; i++)
        {
            history.Add(Text($"entrée {i}"));
        }

        Assert.Equal(ClipboardHistory.Capacity, history.Entries.Count);
        Assert.Equal($"entrée {ClipboardHistory.Capacity}", history.Entries[0].Text);
        Assert.DoesNotContain(history.Entries, e => e.Text == "entrée 0");
    }

    [Fact]
    public void ClearEmptiesTheHistory()
    {
        var history = new ClipboardHistory();
        history.Add(Text("un"));
        history.Clear();

        Assert.Empty(history.Entries);
    }
}

public class ClipboardPreviewTests
{
    private static readonly DateTimeOffset When = DateTimeOffset.UnixEpoch;

    // A copied paragraph shown raw would make one row as tall as the window.
    [Fact]
    public void FlattensLineBreaks()
        => Assert.Equal("une ligne puis une autre", ClipboardEntry.Collapse("une ligne\r\npuis une autre"));

    [Fact]
    public void CollapsesRunsOfSpaces()
        => Assert.Equal("code indenté", ClipboardEntry.Collapse("code        indenté"));

    [Fact]
    public void TrimsTheEnds()
        => Assert.Equal("texte", ClipboardEntry.Collapse("   texte \r\n"));

    [Fact]
    public void CutsWhatIsTooLongAndSaysSo()
    {
        var preview = ClipboardEntry.Collapse(new string('x', 500));

        Assert.Equal(ClipboardEntry.PreviewLength + 1, preview.Length);
        Assert.EndsWith("…", preview);
    }

    [Fact]
    public void LeavesAShortLineAlone()
        => Assert.Equal("court", ClipboardEntry.Collapse("court"));

    // An image entry carries a description, not content to flatten.
    [Fact]
    public void AnImagePreviewIsItsLabel()
    {
        var entry = new ClipboardEntry(ClipboardKind.Image, "Image 800×600", new object(), When);
        Assert.Equal("Image 800×600", entry.Preview);
    }

    [Fact]
    public void ATextPreviewIsCollapsed()
    {
        var entry = new ClipboardEntry(ClipboardKind.Text, "a\r\nb", null, When);
        Assert.Equal("a b", entry.Preview);
    }
}
