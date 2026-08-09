using System.Runtime.InteropServices;

namespace Sc2Xboxed.Windows;

/// <summary>
/// Resolves a function exported by ordinal only, with no name.
/// </summary>
/// <remarks>
/// XInput's two useful extras — the Guide button and the vendor ids — are exported this way, and
/// there is no managed route to them. <see cref="NativeLibrary.TryGetExport"/> takes a <i>name</i>:
/// handing it "#108" looks like the linker convention for an ordinal but is just a string that no
/// export is called, so it fails on every machine and reports the export as missing. That misread
/// cost a working feature and a log line that blamed Windows for it.
///
/// <para>
/// <c>GetProcAddress</c> accepts an ordinal in place of a name pointer, which is what the overload
/// below expresses: an <c>IntPtr</c> whose high word is zero is read as an ordinal rather than as a
/// pointer to a string.
/// </para>
/// </remarks>
public static class NativeOrdinal
{
    /// <summary>The exported function at <paramref name="ordinal"/>, or null when absent.</summary>
    public static TDelegate? Resolve<TDelegate>(string module, int ordinal)
        where TDelegate : Delegate
    {
        try
        {
            var library = LoadLibraryW(module);

            if (library == IntPtr.Zero)
            {
                return null;
            }

            var address = GetProcAddress(library, ordinal);

            return address == IntPtr.Zero
                ? null
                : Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string fileName);

    /// <summary>The ordinal overload: the second argument is a number, not a string pointer.</summary>
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, IntPtr ordinal);

    private static IntPtr GetProcAddress(IntPtr module, int ordinal)
        => GetProcAddress(module, new IntPtr(ordinal));
}
