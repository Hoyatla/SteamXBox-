using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SenSÉ.Windows;

/// <summary>
/// Ties a child process's life to this one's, so that nothing SenSÉ started outlives it.
/// </summary>
/// <remarks>
/// <b>Enforced by the kernel, not by a shutdown path.</b> A job object with
/// <c>KILL_ON_JOB_CLOSE</c> terminates everything in it the moment its last handle closes — and
/// Windows closes that handle when the process holding it ends, however it ends: a clean exit, an
/// unhandled exception, a force-kill, the power going off.
///
/// <para>
/// This is the same shape as the two things in this product that already need no repair at all: a
/// ViGEm pad, which lives only as long as the handle to the bus driver, and the Steam Controller's
/// lizard mode, which comes back the moment the heartbeat stops. Measured on 11 August 2026: six
/// virtual pads present, zero six seconds after a hard kill, with nothing written down and nobody
/// asked to tidy up.
/// </para>
///
/// <para>
/// The alternative was to hunt for strays at the next launch. That needs a record of what was
/// started, a way to tell our stray from a copy the user ran deliberately, and a decision about what
/// to do when the two cannot be told apart — three chances to be wrong about killing somebody's
/// process. This has none.
/// </para>
///
/// <para>
/// <b>What belongs in the job and what does not.</b> Only the processes SenSÉ runs as its own
/// machinery: the core it drives, the overlay keyboard it puts on screen. Not a tool the user
/// launched from a tile — that one is theirs, and closing the environment is not a reason to take it
/// away from them.
/// </para>
/// </remarks>
public static class ChildProcesses
{
    private const uint JobObjectExtendedLimitInformation = 9;
    private const uint LimitKillOnJobClose = 0x00002000;

    private static readonly object Gate = new();

    /// <summary>
    /// The job every adopted child joins.
    /// </summary>
    /// <remarks>
    /// Held for the life of the process and deliberately never closed: closing it is precisely what
    /// kills the children, so the only correct moment to do so is the moment this process ends —
    /// which Windows handles without being asked.
    /// </remarks>
    private static IntPtr _job = IntPtr.Zero;

    private static bool _unavailable;

    /// <summary>
    /// Puts a child under this process's life, and says whether it worked.
    /// </summary>
    /// <remarks>
    /// A failure is reported and never thrown. Not being able to bind a child's lifetime is a
    /// shortcoming of the tidying-up, not a reason to refuse to start the overlay keyboard the user
    /// is waiting for.
    /// </remarks>
    public static bool Adopt(Process child, Action<string>? log = null)
    {
        try
        {
            lock (Gate)
            {
                if (_unavailable)
                {
                    return false;
                }

                if (_job == IntPtr.Zero && !Create(log))
                {
                    return false;
                }

                if (AssignProcessToJobObject(_job, child.Handle))
                {
                    return true;
                }

                // A process already in a job it will not leave. Nested jobs work on every Windows
                // this product supports, so this is rare enough to be worth naming when it happens.
                log?.Invoke(
                    $"child processes: could not bind {child.ProcessName} (pid {child.Id}) to this "
                    + $"session's lifetime: error {Marshal.GetLastWin32Error()}.");

                return false;
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"child processes: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool Create(Action<string>? log)
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);

        if (job == IntPtr.Zero)
        {
            _unavailable = true;
            log?.Invoke($"child processes: no job object available: error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        var information = new JobObjectExtendedLimit
        {
            BasicLimitInformation = new JobObjectBasicLimit { LimitFlags = LimitKillOnJobClose },
        };

        var size = Marshal.SizeOf<JobObjectExtendedLimit>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(information, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                _unavailable = true;
                log?.Invoke(
                    "child processes: the job object would not take the kill-on-close limit: "
                    + $"error {Marshal.GetLastWin32Error()}. Children may outlive this session.");

                CloseHandle(job);
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        _job = job;
        log?.Invoke("child processes: bound to this session's lifetime.");

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimit
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimit
    {
        public JobObjectBasicLimit BasicLimitInformation;
        public IoCounters IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr security, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr job, uint infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
