using System.Runtime.InteropServices;

namespace SenSÉ.Desktop.Search;

/// <summary>
/// Shows an entry inside its folder, through the shell's own interface.
/// </summary>
/// <remarks>
/// Not <c>explorer.exe /select</c>, and that is a measured choice rather than a preference. Naming
/// the explorer on the command line does not open a window at all on the machine this was tested on
/// — not with a path, not with <c>/n</c>, not with <c>/root</c>, not with <c>/select</c>. The process
/// starts, reports a process id, and exits having done nothing, so there is not even a failure to
/// log. It is the worst shape a bug can take: the caller is told it worked.
///
/// <para>
/// <c>SHOpenFolderAndSelectItems</c> is what the explorer's own interface calls, so it does not
/// depend on the command line being handled. Passing an item with no children is the documented way
/// to say "open the folder this lives in, and select this".
/// </para>
/// </remarks>
public static class ShellFolders
{
    /// <summary>Opens the folder holding an entry and selects it. Says whether it worked.</summary>
    public static bool Reveal(string path, Action<string>? log = null)
    {
        var item = IntPtr.Zero;

        try
        {
            SHParseDisplayName(path, IntPtr.Zero, out item, 0, out _);

            if (item == IntPtr.Zero)
            {
                log?.Invoke($"search: the shell does not recognise {path}.");
                return false;
            }

            SHOpenFolderAndSelectItems(item, 0, null, 0);

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"search: revealing {path} failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            if (item != IntPtr.Zero)
            {
                // Allocated by the shell with the COM allocator, so it is freed with that one.
                Marshal.FreeCoTaskMem(item);
            }
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHParseDisplayName(
        string name, IntPtr bindingContext, out IntPtr idList, uint attributesIn, out uint attributesOut);

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHOpenFolderAndSelectItems(
        IntPtr folder, uint count, IntPtr[]? children, uint flags);
}
