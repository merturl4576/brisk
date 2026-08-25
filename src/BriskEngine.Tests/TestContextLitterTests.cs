using System;
using System.IO;
using Xunit;

namespace BriskEngine.Tests;

/// LITTER REGRESSION (round 10): TestContext used to leak one loose
/// brisk-ctx-* directory into %TEMP% per test; thousands accumulated, and
/// the app's own cleaner was found grinding through them for minutes on
/// 2026-08-17. The suite must never litter the machine it tests on.
///
/// Round 12 took the race out of it. The assertion used to count the
/// brisk-ctx-* directories in %TEMP% before and after and demand the two
/// numbers match — a count of a directory the whole machine writes to, read
/// twice, with anything free to create or delete an entry in between. What
/// the test is actually here to guard is narrower and belongs to this
/// context alone: the directory it was given landed under the shared
/// per-run root, not loose in %TEMP% beside it.
public sealed class TestContextLitterTests
{
    [Fact]
    public void Empty_PutsCtxDirs_UnderTheSharedPerRunRoot_NeverLooseInTemp()
    {
        var ctx = TestContext.Empty();

        Assert.True(Directory.Exists(ctx.DataDirectory),
            $"{ctx.DataDirectory} was handed out but never created");

        var parent = Directory.GetParent(ctx.DataDirectory)!.FullName;
        Assert.True(
            string.Equals(parent, TestContext.CtxRoot, StringComparison.OrdinalIgnoreCase),
            $"the context data dir sits under {parent}, not under the shared " +
            $"per-run root {TestContext.CtxRoot} — one run's worth of these is " +
            "what the next run deletes, and only what is inside the root gets " +
            "deleted");
    }
}
