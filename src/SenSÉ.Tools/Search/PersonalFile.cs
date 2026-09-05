using System.Security.Cryptography;
using System.Text;

namespace SenSÉ.Tools.Search;

/// <summary>
/// Reads and writes a file that holds somebody's own business, encrypted at rest.
/// </summary>
/// <remarks>
/// <b>An index of correspondence is a copy of correspondence.</b> The mail index keeps each
/// message's readable text so that searching does not have to reopen a three-hundred-megabyte
/// mailbox for every keystroke — which means eighteen megabytes of somebody's letters sat in
/// <c>%LOCALAPPDATA%</c> in plain text, readable by anything that could open the file. For a product
/// sold to schools, universities and companies handling sensitive material, that is not a detail to
/// settle after the sale.
///
/// <para>
/// Windows' own data protection is used rather than a password. A password on a search index would
/// be asked for on every launch and would therefore be turned off; and a key kept beside the file it
/// protects is not a key. <c>DPAPI</c> derives one from the signed-in Windows account, so the file
/// is readable by that account on that machine and by nothing else — with nothing for the user to
/// remember and nothing for this product to store.
/// </para>
///
/// <para>
/// <b>What it does and does not do.</b> It protects the file: copied to a backup, to a USB stick, to
/// a cloud folder, or read from the disk in another machine, it is unreadable. It does <b>not</b>
/// protect against a program running as that same user, which can simply ask Windows to decrypt it —
/// no local encryption can protect against that, and claiming otherwise would be worse than plain
/// text, because it would be believed.
/// </para>
///
/// <para>
/// A file that cannot be decrypted is treated as absent, not as an error. The index is a cache of a
/// mailbox that still exists, so the cost of losing it — after a Windows account is recreated, or
/// the file is carried to another machine — is a rebuild, not a loss.
/// </para>
/// </remarks>
public static class PersonalFile
{
    /// <summary>What marks a file as written by this, so an older plain one is recognised.</summary>
    private static readonly byte[] Marker = "SXBP1"u8.ToArray();

    /// <summary>Whether this platform can protect a file at all.</summary>
    /// <remarks>
    /// Windows only. The product is Windows-only, but the tools library is not tied to it, and a
    /// silent failure to encrypt would be the worst of both worlds.
    ///
    /// <para>
    /// The guard attribute is what lets the compiler follow this answer to the calls below. Without
    /// it the platform analyser cannot see through a property and objects at every call site — and
    /// the tempting cure, silencing it, would leave nothing checking that the guard is really there.
    /// </para>
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatformGuard("windows")]
    public static bool Available => OperatingSystem.IsWindows();

    /// <summary>
    /// Reads a file this wrote, or one written in plain text before it existed.
    /// </summary>
    /// <param name="wasPlain">
    /// True when the file was still in plain text, so the caller knows to write it back protected.
    /// </param>
    public static string? Read(string path, out bool wasPlain, Action<string>? log = null)
    {
        wasPlain = false;

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = File.ReadAllBytes(path);

            if (!StartsWithMarker(bytes))
            {
                // Written before this existed, or by hand. Read it, and let the caller decide to
                // protect it from now on.
                wasPlain = true;
                return Encoding.UTF8.GetString(bytes);
            }

            if (!Available)
            {
                log?.Invoke("personal file: protected on another system and unreadable here.");
                return null;
            }

            var plain = System.Security.Cryptography.ProtectedData.Unprotect(
                bytes[Marker.Length..], optionalEntropy: null, DataProtectionScope.CurrentUser);

            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Another Windows account, another machine, or a corrupted file. Treated as absent: this
            // is a cache of something that still exists.
            log?.Invoke("personal file: cannot be decrypted here; it will be rebuilt.");
            return null;
        }
        catch (Exception exception)
        {
            log?.Invoke($"personal file unreadable: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// Writes a file so that only this Windows account, on this machine, can read it.
    /// </summary>
    /// <remarks>
    /// Written beside and moved into place, so an interrupted write cannot leave a half-file where a
    /// whole one was. <b>The temporary is protected too</b> — writing the plain form first and
    /// encrypting afterwards would put the correspondence on the disk in clear, which is the thing
    /// being prevented.
    /// </remarks>
    public static bool Write(string path, string content, Action<string>? log = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var plain = Encoding.UTF8.GetBytes(content);

            byte[] bytes;

            if (Available)
            {
                var sealed_ = System.Security.Cryptography.ProtectedData.Protect(
                    plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

                bytes = [.. Marker, .. sealed_];
            }
            else
            {
                // Said out loud rather than done quietly. Somewhere that cannot protect the file is
                // somewhere the user should be told their mail is being written in the clear.
                log?.Invoke(
                    "personal file: this system offers no data protection, so the file is written "
                    + "unprotected.");

                bytes = plain;
            }

            var temporary = path + ".new";

            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);

            return true;
        }
        catch (Exception exception)
        {
            log?.Invoke($"personal file could not be written: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool StartsWithMarker(byte[] bytes)
        => bytes.Length >= Marker.Length && bytes.AsSpan(0, Marker.Length).SequenceEqual(Marker);
}
