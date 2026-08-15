namespace Sc2Xboxed.Core.Input;

/// <summary>
/// Numbers the profiles of one controller family: 1, then 2, and the first free number after a
/// deletion.
/// </summary>
/// <remarks>
/// Each family numbers its own profiles from one. "Manette PS5 1" and "Manette Xbox 1" are two
/// different profiles and both are the first of their family — numbering across families would make
/// the number say nothing about which pad it belongs to.
///
/// <para>
/// <b>A deleted number is refilled before any new one is handed out.</b> Numbering by "one more than
/// the largest" leaves a hole for good: delete 2 out of 1, 2, 3 and the next profile is 4, so the
/// list reads 1, 3, 4 and every profile after that carries a number nobody can account for. Taking
/// the lowest free number keeps the set contiguous, which is the only way the number stays a
/// position the user can count to rather than a serial they have to look up.
/// </para>
///
/// <para>
/// Pure, and deliberately so: it takes the numbers already in use and returns one. It reads no file
/// and knows nothing of profiles, which is what makes the rule testable without a disk, a GUI or a
/// controller.
/// </para>
/// </remarks>
public static class ProfileNumbering
{
    /// <summary>
    /// The number a new profile of this family should take.
    /// </summary>
    /// <param name="taken">
    /// The numbers already used by the family, in any order. Duplicates and numbers below one are
    /// ignored rather than rejected: this is fed by names a user can rename by hand.
    /// </param>
    /// <returns>The lowest free number, which is one more than the largest when the set has no hole.</returns>
    public static int NextFree(IEnumerable<int> taken)
    {
        var used = new HashSet<int>();

        foreach (var number in taken)
        {
            if (number >= 1)
            {
                used.Add(number);
            }
        }

        var candidate = 1;

        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    /// <summary>The name a family's profile carries at a given number.</summary>
    /// <remarks>
    /// One place builds it and one place reads it back, so the two cannot drift. A name written by
    /// one rule and parsed by another is a profile that exists twice or not at all.
    /// </remarks>
    public static string NameFor(string family, int number)
        => string.IsNullOrWhiteSpace(family) ? number.ToString() : $"{family.Trim()} {number}";

    /// <summary>
    /// The number in a family's profile name, or null when the name does not belong to that family.
    /// </summary>
    /// <remarks>
    /// Null for anything the user renamed by hand. Such a profile keeps its name and simply holds no
    /// number — it must not be renumbered behind the user's back, and it must not make the next
    /// number skip either.
    /// </remarks>
    public static int? NumberOf(string family, string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName) || string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var prefix = family.Trim();
        var name = profileName.Trim();

        if (name.Length <= prefix.Length + 1
            || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || name[prefix.Length] != ' ')
        {
            return null;
        }

        return int.TryParse(name[(prefix.Length + 1)..], out var number) && number >= 1
            ? number
            : null;
    }

    /// <summary>The next free name for a family, given the profile names it already has.</summary>
    public static string NextName(string family, IEnumerable<string> existingNames)
        => NameFor(family, NextFree(existingNames.Select(name => NumberOf(family, name) ?? 0)));
}
