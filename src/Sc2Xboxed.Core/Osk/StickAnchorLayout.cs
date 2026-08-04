namespace Sc2Xboxed.Core.Osk;

/// <summary>A key position on the overlay keyboard.</summary>
public readonly record struct KeyCell(int Column, int Row);

/// <summary>One resting point a stick aims from, and the band of keys it reaches.</summary>
/// <param name="Home">Key the stick points at when centred.</param>
/// <param name="FirstColumn">Leftmost column the anchor reaches.</param>
/// <param name="LastColumn">Rightmost column the anchor reaches.</param>
public readonly record struct StickAnchor(KeyCell Home, int FirstColumn, int LastColumn);

/// <summary>
/// Places two anchors per stick on the overlay keyboard, and resolves a stick push to a key.
/// </summary>
/// <remarks>
/// A stick has a bounded, circular throw; the keyboard is a wide rectangle. Aiming the whole board
/// from one central anchor gives a hopeless ratio — a dozen columns of travel against five rows, so
/// a small horizontal push crosses three keys while the same vertical push crosses one. Selecting a
/// key becomes a matter of luck at the edges.
///
/// One anchor per stick fixes that: the left stick owns the left half of the board, the right stick
/// the right half. Each band is about six columns against five rows, which is as close to equal
/// reach in every direction as this board allows.
///
/// Neither stick ever changes anchor. Both are live at once, each on its own half, so a word is
/// typed by alternating hands rather than by walking one selection across the keyboard. That is
/// also why there is nothing to switch: the anchor a stick uses is decided by which stick it is.
///
/// A centred stick means its anchor's key: the stick springs back on its own, and the selection
/// coming to rest somewhere predictable is what makes typing without watching possible.
/// </remarks>
public static class StickAnchorLayout
{
    /// <summary>Anchors given to each stick. One: a stick never changes anchor.</summary>
    public const int AnchorsPerStick = 1;

    /// <summary>Anchors over the whole board: one per stick.</summary>
    public const int AnchorCount = AnchorsPerStick * 2;

    /// <summary>
    /// Builds the anchors for a board of the given size.
    /// </summary>
    /// <param name="columns">Widest row's key count.</param>
    /// <param name="rows">Number of rows.</param>
    /// <remarks>
    /// Bands are split so the remainder lands on the leftmost ones rather than piling onto the last:
    /// a board of thirteen columns gives 4, 3, 3, 3 instead of 3, 3, 3, 4, which would make the
    /// right-hand band reach further than the others for no reason.
    /// </remarks>
    public static IReadOnlyList<StickAnchor> Build(int columns, int rows)
    {
        if (columns < AnchorCount || rows < 1)
        {
            // Too narrow to divide. One anchor over the whole board is still usable, and refusing
            // to build would leave the keyboard unnavigable rather than merely coarse.
            return [new StickAnchor(new KeyCell(columns / 2, rows / 2), 0, Math.Max(0, columns - 1))];
        }

        var anchors = new List<StickAnchor>(AnchorCount);
        var baseWidth = columns / AnchorCount;
        var remainder = columns % AnchorCount;
        var start = 0;

        for (var i = 0; i < AnchorCount; i++)
        {
            var width = baseWidth + (i < remainder ? 1 : 0);
            var last = start + width - 1;

            anchors.Add(new StickAnchor(
                new KeyCell((start + last) / 2, (rows - 1) / 2),
                start,
                last));

            start = last + 1;
        }

        return anchors;
    }

    /// <summary>The anchor a stick aims from. The left stick takes the left half of the board.</summary>
    public static int AnchorFor(bool leftStick) => leftStick ? 0 : 1;

    /// <summary>
    /// Resolves a stick position to the key it points at.
    /// </summary>
    /// <param name="anchor">Anchor the stick is aiming from.</param>
    /// <param name="x">Horizontal deflection, -1 to 1.</param>
    /// <param name="y">Vertical deflection, -1 to 1, positive upwards.</param>
    /// <param name="rows">Number of rows on the board.</param>
    /// <remarks>
    /// Full deflection reaches the edge of the band exactly, so the whole throw is used and no
    /// column needs a push the stick cannot produce. Y is inverted here because sticks report up as
    /// positive while rows count downwards.
    /// </remarks>
    public static KeyCell Resolve(StickAnchor anchor, double x, double y, int rows)
    {
        // Each side is scaled to its own distance from the anchor, rather than both to half the
        // band. A band of six columns puts its anchor on column 2 while its middle is at 2.5, so a
        // symmetric scale leaves the far column reachable only at exactly full deflection — a knife
        // edge in practice, and a column the user would swear was broken.
        //
        // Uneven gain either side is the price, and it is the right one: rest lands on the anchor,
        // and both edges are reached with the same push.
        var column = Reach(Math.Clamp(x, -1, 1), anchor.Home.Column, anchor.FirstColumn, anchor.LastColumn);

        // Y is negated because sticks report up as positive while rows count downwards.
        var row = Reach(-Math.Clamp(y, -1, 1), anchor.Home.Row, 0, rows - 1);

        return new KeyCell(
            Math.Clamp((int)Math.Round(column, MidpointRounding.AwayFromZero), anchor.FirstColumn, anchor.LastColumn),
            Math.Clamp((int)Math.Round(row, MidpointRounding.AwayFromZero), 0, rows - 1));
    }

    /// <summary>Maps a -1..1 push onto the distance available on that side of the anchor.</summary>
    private static double Reach(double push, int home, int first, int last)
        => push < 0
            ? home + push * (home - first)
            : home + push * (last - home);
}
