using SenSÉ.Core.Diagnostics;
using Xunit;

namespace SenSÉ.Core.Tests;

/// <summary>
/// The session record of a window process.
/// </summary>
/// <remarks>
/// Two claims are being tested, and only one of them is about content. The first is that the log
/// records what happened on screen. The second is that it cannot itself become a fault — it is
/// called from exception handlers and from static helpers running while the application is coming
/// apart, so a diagnostic that throws there would destroy the very session it exists to explain.
///
/// The tests share one static logger and therefore run in sequence, which xUnit does by default
/// within a class. Marked explicitly all the same, because the reason is not obvious from reading
/// them and a later reader parallelising the collection would get failures that look like defects
/// in the logger.
/// </remarks>
[Collection("UiLog")]
public class UiLogTests : IDisposable
{
    public void Dispose() => UiLog.Stop("test finished");

    private static string ReadBack()
    {
        var path = UiLog.Path;
        Assert.NotNull(path);

        UiLog.Stop("reading back");

        return File.ReadAllText(path);
    }

    // The point of the class. Everything below is a variation on it.
    [Fact]
    public void WhatHappenedOnScreenIsWrittenDown()
    {
        UiLog.Start("test-screen");
        UiLog.Window("MainWindow", "shown", "environment overlay");
        UiLog.Action("Calculator");
        UiLog.Process("SenSÉ.Core", "started, PID 1234");

        var text = ReadBack();

        Assert.Contains("window MainWindow: shown (environment overlay)", text);
        Assert.Contains("action Calculator", text);
        Assert.Contains("process SenSÉ.Core: started, PID 1234", text);
    }

    // The method the class was written for: a swallowed exception is exactly the failure nobody can
    // describe afterwards, so the catch stays and the silence goes.
    [Fact]
    public void ASwallowedFailureLeavesItsTypeAndMessage()
    {
        UiLog.Start("test-failure");
        UiLog.Failure("starting the bridge", new InvalidOperationException("no executable"));

        var text = ReadBack();

        Assert.Contains("starting the bridge failed", text);
        Assert.Contains(nameof(InvalidOperationException), text);
        Assert.Contains("no executable", text);
    }

    [Fact]
    public void ACrashKeepsItsStackTrace()
    {
        UiLog.Start("test-crash");

        Exception caught;
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        UiLog.Crash("unhandled exception", caught);

        var text = ReadBack();

        Assert.Contains("boom", text);
        Assert.Contains(nameof(UiLogTests), text);
    }

    // "Closed by the user" and "exited while restarting for a theme" are the same silence otherwise,
    // and only one of them is a bug.
    [Fact]
    public void TheSessionEndRecordsWhy()
    {
        UiLog.Start("test-end");
        var path = UiLog.Path!;
        UiLog.Stop("restarting for a theme change");

        Assert.Contains("session end: restarting for a theme change", File.ReadAllText(path));
    }

    [Fact]
    public void TheSessionStartRecordsWhichBuildWroteIt()
    {
        UiLog.Start("test-identity");

        var text = ReadBack();

        Assert.Contains("session start", text);
        Assert.Contains("process id", text);
        Assert.Contains("base directory", text);
    }

    // Called before anything is set up, from a handler running as the process dies, and after the
    // log is already closed. None of those may throw: a diagnostic that can fail is a liability.
    [Fact]
    public void NothingThrowsBeforeStart()
    {
        UiLog.Stop("not started");

        UiLog.Info("before");
        UiLog.Warn("before");
        UiLog.Error("before");
        UiLog.Debug("before");
        UiLog.Window("W", "shown");
        UiLog.Action("A");
        UiLog.Process("P", "started");
        UiLog.Failure("something", new Exception("x"));
        UiLog.Crash("something", new Exception("x"));

        Assert.Null(UiLog.Path);
    }

    [Fact]
    public void NothingThrowsAfterStop()
    {
        UiLog.Start("test-after-stop");
        UiLog.Stop("done");

        UiLog.Info("after");
        UiLog.Failure("after", new Exception("x"));
        UiLog.Stop("again");

        Assert.Null(UiLog.Path);
    }

    // Two calls to Start would otherwise truncate the file and lose the first half of the session —
    // the half most likely to explain the second.
    [Fact]
    public void StartingTwiceKeepsTheFirstLog()
    {
        UiLog.Start("test-idempotent");
        UiLog.Info("first line");
        var path = UiLog.Path;

        UiLog.Start("test-idempotent-again");

        Assert.Equal(path, UiLog.Path);
        Assert.Contains("first line", ReadBack());
    }

    [Fact]
    public void EachProcessWritesItsOwnFile()
    {
        UiLog.Start("test-alpha");
        var alpha = UiLog.Path;
        UiLog.Stop("switching");

        UiLog.Start("test-beta");
        var beta = UiLog.Path;

        Assert.NotEqual(alpha, beta);
        Assert.Contains("test-alpha", alpha);
        Assert.Contains("test-beta", beta);
    }

    [Fact]
    public void AnEmptyProcessNameStillProducesALog()
    {
        UiLog.Start("   ");

        Assert.NotNull(UiLog.Path);
        Assert.Contains("session start", ReadBack());
    }
}
