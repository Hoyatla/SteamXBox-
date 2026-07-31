namespace Sc2Xboxed.Osk;

/// <summary>
/// Daisywheel layout: eight petals of four characters. The left pad direction selects the petal and
/// ABXY selects the slot inside it, which is the Steam gesture for fast controller typing.
/// </summary>
public static class DaisywheelLayout
{
    public const int Petals = 8;
    public const int SlotsPerPetal = 4;

    /// <summary>
    /// Radius below which no petal is selected. Without it the wheel would flicker between
    /// neighbours whenever the finger rests near the centre.
    /// </summary>
    public const double SelectionDeadZone = 0.35;

    /// <summary>Slot labels in ABXY order, matching the on-screen hints.</summary>
    public static readonly string[] SlotNames = ["A", "B", "X", "Y"];

    /// <summary>Petal labels clockwise from north, for logging and layout maths.</summary>
    public static readonly string[] PetalNames = ["N", "NE", "E", "SE", "S", "SW", "W", "NW"];

    // The last petal is reserved for editing actions on both pages, so it stays in the same place
    // whichever page is active.
    private static readonly KeyDef[][] LetterPage = BuildPage(
    [
        "abcd",
        "efgh",
        "ijkl",
        "mnop",
        "qrst",
        "uvwx",
        "yz,.",
    ]);

    private static readonly KeyDef[][] SymbolPage = BuildPage(
    [
        "1234",
        "5678",
        "90-_",
        "@#$%",
        "&*()",
        "+=/\\",
        "'\"?!",
    ]);

    /// <summary>Returns the four slots of a petal, or null when the index is out of range.</summary>
    public static KeyDef[]? Petal(int petalIndex, bool symbolPage)
    {
        if (petalIndex < 0 || petalIndex >= Petals)
        {
            return null;
        }

        return (symbolPage ? SymbolPage : LetterPage)[petalIndex];
    }

    public static KeyDef? Slot(int petalIndex, int slotIndex, bool symbolPage)
    {
        var petal = Petal(petalIndex, symbolPage);
        if (petal is null || slotIndex < 0 || slotIndex >= petal.Length)
        {
            return null;
        }

        return petal[slotIndex];
    }

    /// <summary>
    /// Maps a pad position to a petal index, clockwise from north, or null inside the dead zone.
    /// Pad Y grows downwards, so it is negated to get standard maths orientation.
    /// </summary>
    public static int? PetalFromPad(double x, double y)
    {
        double radius = Math.Sqrt(x * x + y * y);
        if (radius < SelectionDeadZone)
        {
            return null;
        }

        double degrees = Math.Atan2(-y, x) * 180.0 / Math.PI;

        // North sits at 90 degrees and indices advance clockwise.
        double fromNorth = 90.0 - degrees;
        int index = (int)Math.Round(fromNorth / 45.0) % Petals;
        if (index < 0)
        {
            index += Petals;
        }

        return index;
    }

    private static KeyDef[][] BuildPage(string[] characterPetals)
    {
        var page = new KeyDef[Petals][];

        for (int petal = 0; petal < characterPetals.Length; petal++)
        {
            var characters = characterPetals[petal];
            var slots = new KeyDef[SlotsPerPetal];

            for (int slot = 0; slot < SlotsPerPetal; slot++)
            {
                char normal = characters[slot];
                char shifted = char.IsLetter(normal) ? char.ToUpperInvariant(normal) : normal;
                slots[slot] = new KeyDef(petal, slot, normal.ToString(), normal, shifted);
            }

            page[petal] = slots;
        }

        // Editing petal, identical on both pages.
        page[Petals - 1] =
        [
            new KeyDef(Petals - 1, 0, "ESPACE", ' ', ' ', ' ', SpecialAction.Space),
            new KeyDef(Petals - 1, 1, "BSP", '\0', '\0', '\0', SpecialAction.Backspace),
            new KeyDef(Petals - 1, 2, "ENTRÉE", '\0', '\0', '\0', SpecialAction.Enter),
            new KeyDef(Petals - 1, 3, "SYM", '\0', '\0', '\0', SpecialAction.Sym),
        ];

        return page;
    }
}
