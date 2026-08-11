using System.Text;
using SteamXBox.Tools.Search;
using Xunit;

namespace Sc2Xboxed.Core.Tests;

/// <summary>
/// The file that holds somebody's correspondence, and what it takes to read it.
/// </summary>
/// <remarks>
/// The mail index keeps each message's readable text so that a search does not reopen a
/// three-hundred-megabyte mailbox for every keystroke. That makes it a copy of somebody's letters,
/// and it sat in <c>%LOCALAPPDATA%</c> in plain text — eighteen megabytes of it on the machine this
/// was written against — readable by anything able to open a file.
///
/// <para>
/// The migration is tested because it runs <b>once per machine</b>, on real correspondence, and a
/// mistake in it is unrecoverable in the direction that matters: it would leave the plain copy in
/// place while reporting success.
/// </para>
/// </remarks>
public class PersonalFileTests
{
    private static string Fresh() => Path.Combine(
        Path.GetTempPath(),
        $"steamxbox-personal-{Guid.NewGuid():N}.json");

    [Fact]
    public void WhatGoesInComesBackOut()
    {
        var path = Fresh();

        try
        {
            Assert.True(PersonalFile.Write(path, "objet: déjeuner\nde: quelqu'un"));

            var back = PersonalFile.Read(path, out var wasPlain);

            Assert.Equal("objet: déjeuner\nde: quelqu'un", back);
            Assert.False(wasPlain);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // The point of the exercise: the words must not be in the file.
    [Fact]
    public void TheWordsAreNotInTheFile()
    {
        var path = Fresh();

        try
        {
            PersonalFile.Write(path, "rendez-vous chez le notaire jeudi");

            var raw = File.ReadAllBytes(path);

            Assert.DoesNotContain("notaire", Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
            Assert.DoesNotContain("notaire", Encoding.Latin1.GetString(raw), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // A file written before any of this existed still has to be readable, and has to be recognised
    // as plain so the caller writes it back protected instead of leaving it.
    [Fact]
    public void APlainFileIsReadAndReported()
    {
        var path = Fresh();

        try
        {
            File.WriteAllText(path, "[{\"Subject\":\"ancien\"}]");

            var back = PersonalFile.Read(path, out var wasPlain);

            Assert.Equal("[{\"Subject\":\"ancien\"}]", back);
            Assert.True(wasPlain, "an unprotected file must be reported, or it is never replaced");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // And once written back, it must no longer be reported as plain — otherwise every load would
    // rewrite a seventeen-megabyte file for nothing.
    [Fact]
    public void ARewrittenFileIsNoLongerPlain()
    {
        var path = Fresh();

        try
        {
            File.WriteAllText(path, "quelque chose");
            PersonalFile.Read(path, out var firstTime);
            Assert.True(firstTime);

            PersonalFile.Write(path, "quelque chose");
            PersonalFile.Read(path, out var secondTime);

            Assert.False(secondTime);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingFileIsNothingRatherThanAnError()
    {
        var back = PersonalFile.Read(Fresh(), out var wasPlain);

        Assert.Null(back);
        Assert.False(wasPlain);
    }

    // A file this cannot decrypt — another Windows account, another machine — is treated as absent.
    // The index is a cache of a mailbox that still exists, so the cost is a rebuild, not a loss.
    [Fact]
    public void AnUndecryptableFileIsTreatedAsAbsent()
    {
        var path = Fresh();

        try
        {
            // The marker, then bytes that are not a protected blob.
            File.WriteAllBytes(path, [.. "SXBP1"u8.ToArray(), 1, 2, 3, 4, 5, 6, 7, 8]);

            Assert.Null(PersonalFile.Read(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
