using System;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

/// The one walk, shared. Two rules now want the same folders measured —
/// disk-breakdown for the totals, large-files for the names — and a folder
/// like %LOCALAPPDATA% costs seconds to walk. The memo is what keeps the
/// second rule free rather than doubling the scan.
public class FileStatsTests
{
    [Fact]
    public void TheSameFolder_IsWalkedOncePerScan()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[@"C:\root"] = 42;

        var first = FileStats.Of(ctx, @"C:\root");
        var second = FileStats.Of(ctx, @"C:\root");

        Assert.Equal(42, first.Bytes);
        Assert.Equal(42, second.Bytes);
        Assert.Equal(1, files.StatsCalls);
    }

    /// Different folders are different answers — a memo that confused them
    /// would report one folder's files under another's name.
    [Fact]
    public void DifferentFolders_AreWalkedSeparately()
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        files.Sizes[@"C:\a"] = 1;
        files.Sizes[@"C:\b"] = 2;

        Assert.Equal(1, FileStats.Of(ctx, @"C:\a").Bytes);
        Assert.Equal(2, FileStats.Of(ctx, @"C:\b").Bytes);
        Assert.Equal(2, files.StatsCalls);
    }

    /// A memo lives as long as the context does, and the context is built
    /// once per scan — so a second scan re-measures rather than answering
    /// from the first one's numbers.
    [Fact]
    public void EachScansContext_CarriesItsOwnMemo()
    {
        var files = (FakeFiles)TestContext.Empty().Files;

        var one = TestContext.Empty();
        ((FakeFiles)one.Files).Sizes[@"C:\root"] = 7;
        FileStats.Of(one, @"C:\root");

        var two = TestContext.Empty();
        ((FakeFiles)two.Files).Sizes[@"C:\root"] = 9;

        Assert.Equal(9, FileStats.Of(two, @"C:\root").Bytes);
        Assert.Equal(1, ((FakeFiles)one.Files).StatsCalls);
        Assert.Equal(1, ((FakeFiles)two.Files).StatsCalls);
        Assert.Equal(0, files.StatsCalls);
    }

    /// The floor and the cut are declared once, here, because two rules and
    /// the memo key all have to agree on them: a caller asking for a
    /// different floor would silently get the first caller's answer.
    [Fact]
    public void TheFloorAndTheCut_AreDeclaredOnce()
    {
        Assert.Equal(500L << 20, FileStats.MinFileBytes);
        Assert.Equal(10, FileStats.Take);
    }
}
