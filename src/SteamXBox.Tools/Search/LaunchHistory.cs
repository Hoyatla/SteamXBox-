namespace SteamXBox.Tools.Search;

/// <summary>How often something has been opened, and when it last was.</summary>
public sealed record LaunchRecord(int Count, DateTime LastOpened);

/// <summary>
/// What the user actually opens, which is the best predictor of what they want next.
/// </summary>
/// <remarks>
/// This is what separates a launcher that finds things from one that guesses well. Ranking on the
/// query alone treats a program opened forty times exactly like one that has never been touched, so
/// the same three keystrokes surface the same wrong answer every day and the user learns to type
/// more of the name instead.
///
/// <para>
/// A nudge, never a verdict. The bonus is capped well below what an exact name match is worth, so
/// history reorders entries that answer the query about equally and cannot promote something the
/// user did not ask for. A launcher that starts second-guessing the query is worse than one that
/// never learned anything.
/// </para>
///
/// <para>
/// No file access here. The environment reads and writes it; this decides what it means, which is
/// what can be tested.
/// </para>
/// </remarks>
public sealed class LaunchHistory
{
    /// <summary>Most a frequently used entry can gain.</summary>
    /// <remarks>
    /// Against 1200 for an exact name match and 900 for a prefix, 300 moves an entry within its
    /// class without ever lifting it over a better answer.
    /// </remarks>
    public const int MaxFrequencyBonus = 300;

    /// <summary>Most a recently used entry can gain, fading over a fortnight.</summary>
    public const int MaxRecencyBonus = 120;

    private readonly Dictionary<string, LaunchRecord> _records;

    public LaunchHistory(IReadOnlyDictionary<string, LaunchRecord>? saved = null)
        => _records = saved is null
            ? new Dictionary<string, LaunchRecord>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, LaunchRecord>(saved, StringComparer.OrdinalIgnoreCase);

    /// <summary>Everything remembered, for the environment to write out.</summary>
    public IReadOnlyDictionary<string, LaunchRecord> All => _records;

    /// <summary>Notes that something was opened.</summary>
    public void Record(string path, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _records[path] = _records.TryGetValue(path, out var existing)
            ? existing with { Count = existing.Count + 1, LastOpened = now }
            : new LaunchRecord(1, now);
    }

    /// <summary>What to add to an entry's score.</summary>
    /// <remarks>
    /// Frequency grows quickly then flattens — the difference between never and twice matters far
    /// more than between forty and fifty times — and recency fades over a fortnight so a tool used
    /// intensely for one project stops crowding the list a month later.
    /// </remarks>
    public int Bonus(string path, DateTime now)
    {
        if (!_records.TryGetValue(path, out var record))
        {
            return 0;
        }

        var frequency = (int)(MaxFrequencyBonus * (1.0 - (1.0 / (1.0 + record.Count))));

        var days = (now - record.LastOpened).TotalDays;
        var recency = days < 0 ? MaxRecencyBonus : (int)Math.Clamp(MaxRecencyBonus * (1.0 - (days / 14.0)), 0, MaxRecencyBonus);

        return frequency + recency;
    }

    /// <summary>
    /// Drops entries that no longer exist or have not been used in a long time.
    /// </summary>
    /// <remarks>
    /// Called with the paths still in the index. Without this the file grows for ever and keeps
    /// ranking things that were uninstalled months ago — a launcher confidently offering a program
    /// that is gone is worse than one that forgot it.
    /// </remarks>
    public void Forget(Func<string, bool> stillExists, DateTime now, int afterDays = 180)
    {
        foreach (var path in _records.Keys.ToList())
        {
            var record = _records[path];

            if (!stillExists(path) || (now - record.LastOpened).TotalDays > afterDays)
            {
                _records.Remove(path);
            }
        }
    }
}
