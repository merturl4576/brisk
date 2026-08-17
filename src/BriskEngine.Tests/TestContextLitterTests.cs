using System;
using System.IO;
using Xunit;

namespace BriskEngine.Tests;

/// LITTER REGRESSION (round 10): TestContext used to leak one loose
/// brisk-ctx-* directory into %TEMP% per test; thousands accumulated, and
/// the app's own cleaner was found grinding through them for minutes on
/// 2026-08-17. The suite must never litter the machine it tests on.
public sealed class TestContextLitterTests
{
    [Fact]
    public void Empty_PutsCtxDirs_UnderTheSharedPerRunRoot_NeverLooseInTemp()
    {
        var looseBefore = Directory.GetDirectories(
            Path.GetTempPath(), "brisk-ctx-*").Length;

        var ctx = TestContext.Empty();

        Assert.StartsWith(TestContext.CtxRoot, ctx.DataDirectory,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(ctx.DataDirectory));
        Assert.Equal(looseBefore, Directory.GetDirectories(
            Path.GetTempPath(), "brisk-ctx-*").Length);
    }
}
