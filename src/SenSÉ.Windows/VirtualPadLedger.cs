using System.Diagnostics;

namespace SenSÉ.Windows;

/// <summary>
/// Keeps a list of the device nodes SenSÉ's own virtual pads have occupied.
/// </summary>
/// <remarks>
/// Windows records every device that has ever appeared and keeps the record forever, so that a
/// driver and its settings survive the device being unplugged. That is deliberate and cannot be
/// declined. SenSÉ creates a virtual Xbox pad each time it enters Xbox mode, and each one leaves
/// three records behind: the pad itself, and an XInput and a HID interface whose instance numbers
/// Windows mints fresh every time. Measured on 12 August: twenty-nine such records had accumulated,
/// and three more appeared during a single session after they were cleared.
///
/// <para>
/// They are not harmless. SenSÉ matches an XInput slot to a physical pad by enumerating XUSB
/// devices; faced with a crowd of indistinguishable candidates it gives up and says so —
/// <c>9 XUSB devices and no way to say which; keeping the slot</c> — after which one controller's
/// input can be attributed to another.
/// </para>
///
/// <para>
/// <b>Why a ledger and not a rule.</b> The obvious cleanup is "remove every absent
/// <c>VID_045E&amp;PID_028E</c>", and that is what the first pass over this machine did. It works
/// here and it is a guess: that vendor and product pair is a real wired Xbox 360 controller, because
/// being indistinguishable from one is the entire purpose of the emulation. On a customer's machine
/// the same rule would also erase the record of a controller they own. Harmless — Windows recreates
/// it on the next plug — but it is not our record to erase. Writing down what we created turns the
/// guess into a list.
/// </para>
///
/// <para>
/// <b>Why nothing here removes anything on its own.</b> Removing a device record needs
/// administrator, and SenSÉ deliberately does not ask for it. So this records during ordinary
/// use, unelevated, and <see cref="Cleanup"/> is called from the places that are already elevated —
/// the uninstaller above all, where leaving records behind is exactly the fault the 3.2 uninstaller
/// committed with its startup entry.
/// </para>
/// </remarks>
public static class VirtualPadLedger
{
    /// <summary>Beside the other state SenSÉ keeps for the user, not in the program folder.</summary>
    /// <remarks>
    /// The program folder is read-only for an unelevated process once the product is installed under
    /// Program Files, and this is written during ordinary use.
    /// </remarks>
    public static string Path { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SenSÉ",
        "virtual-pads.txt");

    /// <summary>
    /// Watches one pad being created and writes down the nodes it brought into being.
    /// </summary>
    /// <remarks>
    /// Scope-based because the device does not exist until the connection completes: the difference
    /// between what was on the bus before and after is what we made. Anything else already there
    /// belongs to another application using the same bus, and is none of our business.
    /// </remarks>
    public static IDisposable RecordCreation(Action<string>? log = null)
        => new Creation(EmulatedPadNodes(), log);

    /// <summary>Every node currently on the virtual pad bus, ours or anyone's.</summary>
    private static HashSet<string> EmulatedPadNodes()
    {
        var nodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var path in XInputDurableIdentity.XusbInterfacePaths(null))
            {
                foreach (var node in DeviceTree.EmulatedPadNodes(path))
                {
                    nodes.Add(node);
                }
            }
        }
        catch
        {
            // An empty reading records nothing rather than recording everything. Failing to write a
            // node down leaves one record behind; writing down somebody else's invites removing it.
        }

        return nodes;
    }

    private sealed class Creation(HashSet<string> before, Action<string>? log) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                var appeared = EmulatedPadNodes().Where(node => !before.Contains(node)).ToList();

                if (appeared.Count == 0)
                {
                    return;
                }

                Remember(appeared);
                log?.Invoke($"virtual pad ledger: {appeared.Count} device node(s) recorded for cleanup.");
            }
            catch (Exception failure)
            {
                // Never worth failing a pad creation over. The cost of losing this is a stale record
                // at uninstall, which is where the product already was before the ledger existed.
                log?.Invoke($"virtual pad ledger: could not record ({failure.GetType().Name}: {failure.Message}).");
            }
        }
    }

    private static void Remember(IEnumerable<string> nodes)
    {
        var known = Read().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = nodes.Where(node => known.Add(node)).ToList();

        if (added.Count == 0)
        {
            return;
        }

        var folder = System.IO.Path.GetDirectoryName(Path);

        if (folder is { Length: > 0 })
        {
            Directory.CreateDirectory(folder);
        }

        File.AppendAllLines(Path, added);
    }

    /// <summary>The nodes recorded so far, in the order they were written.</summary>
    public static IReadOnlyList<string> Read()
    {
        try
        {
            return File.Exists(Path)
                ? File.ReadAllLines(Path).Where(line => line.Trim().Length > 0).ToList()
                : [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Removes the recorded device records. Needs administrator; reports rather than throws.
    /// </summary>
    /// <returns>How many were removed, and how many resisted.</returns>
    public static (int Removed, int Refused) Cleanup(Action<string> log)
    {
        var recorded = Read();

        if (recorded.Count == 0)
        {
            log("virtual pad cleanup: nothing recorded.");
            return (0, 0);
        }

        log($"virtual pad cleanup: {recorded.Count} recorded device node(s).");

        var removed = 0;
        var stubborn = new List<string>();

        foreach (var node in recorded)
        {
            if (RemoveDevice(node, log))
            {
                removed++;
            }
            else
            {
                stubborn.Add(node);
            }
        }

        // What could not go stays written down. A record dropped from the ledger because one attempt
        // failed is a record nobody will ever try to remove again.
        try
        {
            if (stubborn.Count == 0)
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            else
            {
                File.WriteAllLines(Path, stubborn);
            }
        }
        catch (Exception failure)
        {
            log($"virtual pad cleanup: could not rewrite the ledger ({failure.GetType().Name}: {failure.Message}).");
        }

        log($"virtual pad cleanup: removed {removed}, refused {stubborn.Count}.");

        return (removed, stubborn.Count);
    }

    private static bool RemoveDevice(string instanceId, Action<string> log)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("pnputil.exe")
            {
                Arguments = $"/remove-device \"{instanceId}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                log($"virtual pad cleanup: could not start pnputil for {instanceId}.");
                return false;
            }

            // Bounded. A tool that never returns must not hold up an uninstall.
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                log($"virtual pad cleanup: pnputil did not finish for {instanceId}.");
                return false;
            }

            if (process.ExitCode == 0)
            {
                return true;
            }

            log($"virtual pad cleanup: pnputil refused {instanceId} (exit {process.ExitCode}).");
            return false;
        }
        catch (Exception failure)
        {
            log($"virtual pad cleanup: {instanceId} — {failure.GetType().Name}: {failure.Message}");
            return false;
        }
    }
}
