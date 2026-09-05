using SenSÉ.Core.Osk;

namespace SenSÉ.App.Console;

/// <summary>
/// Reads whether a keyboard overlay is actually on screen.
/// </summary>
/// <remarks>
/// The overlay refreshes a file while it is visible. Nothing deletes it on a crash and nothing has
/// to: freshness is the answer, so a process that stops beating stops being believed a couple of
/// seconds later. See <see cref="OskInstanceNaming.VisibleBeatFile"/> for why it is a beat.
///
/// <para>
/// <b>Cached, because the caller is a frame loop.</b> This is asked once per frame per controller,
/// at rates measured between 65 and 545 a second. A file stat at that rate, for an answer that
/// cannot meaningfully change inside a third of a second, would be a disk touched a hundred thousand
/// times a session for nothing.
/// </para>
/// </remarks>
internal static class OskPresence
{
    /// <summary>
    /// How old a beat may be and still count as showing.
    /// </summary>
    /// <remarks>
    /// The overlay beats every 400 ms. Two seconds is several missed beats — enough that a busy
    /// machine or a slow disk is not mistaken for a keyboard that has gone.
    /// </remarks>
    private static readonly TimeSpan StillWarm = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan RecheckAfter = TimeSpan.FromMilliseconds(300);

    private static readonly Dictionary<string, (DateTimeOffset At, bool Showing)> Answers = [];

    public static bool IsShowing(OskInstanceNaming naming, DateTimeOffset now)
    {
        var key = naming.Suffix;

        if (Answers.TryGetValue(key, out var last) && now - last.At < RecheckAfter)
        {
            return last.Showing;
        }

        var showing = Read(naming, now);
        Answers[key] = (now, showing);

        return showing;
    }

    private static bool Read(OskInstanceNaming naming, DateTimeOffset now)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, naming.VisibleBeatFile);
            var info = new FileInfo(path);

            // Absent is a perfectly good answer, and the ordinary one: the overlay removes the file
            // when it hides on purpose rather than making the core wait out the staleness window.
            return info.Exists && now - info.LastWriteTimeUtc < StillWarm;
        }
        catch
        {
            // Unreadable is not "showing". Erring the other way would suspend the pointer on a
            // locked file, which is the failure this whole mechanism exists to end.
            return false;
        }
    }
}
