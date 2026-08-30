using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public class BootTrendTests
{
    private static BootRecord Boot(int daysAgo, int ms) =>
        new(Anchor.AddDays(-daysAgo), ms, null, Array.Empty<BootOffender>());

    private static readonly DateTime Anchor = new(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NoChangesRecorded_NoTrend()
    {
        var boots = new List<BootRecord> { Boot(1, 50_000), Boot(2, 50_000), Boot(3, 50_000) };
        Assert.Null(BootTrendCalculator.Compute(boots, null, null));
    }

    [Fact]
    public void FewerThanThreeBootsBefore_IsAnAnecdote_NotATrend()
    {
        var boots = new List<BootRecord> { Boot(10, 59_000), Boot(9, 60_000), Boot(1, 41_000) };
        Assert.Null(BootTrendCalculator.Compute(boots,
            Anchor.AddDays(-5), Anchor.AddDays(-5)));
    }

    [Fact]
    public void NoBootSinceTheLastChange_NoTrend()
    {
        var boots = new List<BootRecord>
            { Boot(10, 59_000), Boot(9, 60_000), Boot(8, 58_000) };
        Assert.Null(BootTrendCalculator.Compute(boots,
            Anchor.AddDays(-5), Anchor.AddDays(-5)));
    }

    /// The announcement's sentence: medians on both sides (lower middle on
    /// an even sample, the degradation rule's convention), counts carried,
    /// and a boot taken BETWEEN the first and last change classified as
    /// neither machine — dropped.
    [Fact]
    public void Computes_LowerMiddleMedians_AndDropsBetweenWindowBoots()
    {
        var boots = new List<BootRecord>
        {
            Boot(12, 61_000), Boot(11, 59_000), Boot(10, 57_000), Boot(9, 63_000),
            Boot(5, 48_000),   // between first and last change — measures neither
            Boot(2, 43_000), Boot(1, 41_000),
        };
        var trend = BootTrendCalculator.Compute(boots,
            Anchor.AddDays(-7), Anchor.AddDays(-3));

        Assert.NotNull(trend);
        Assert.Equal(59_000, trend!.BeforeMedianMs);   // lower middle of 57,59,61,63
        Assert.Equal(4, trend.BeforeBoots);
        Assert.Equal(41_000, trend.AfterMedianMs);     // lower middle of 41,43
        Assert.Equal(2, trend.AfterBoots);
    }

    [Fact]
    public void OneBootAfter_IsEnough_TheCountSaysHowThin()
    {
        var boots = new List<BootRecord>
        {
            Boot(12, 61_000), Boot(11, 59_000), Boot(10, 57_000),
            Boot(1, 41_000),
        };
        var trend = BootTrendCalculator.Compute(boots,
            Anchor.AddDays(-5), Anchor.AddDays(-5));

        Assert.NotNull(trend);
        Assert.Equal(1, trend!.AfterBoots);
        Assert.Equal(41_000, trend.AfterMedianMs);
    }
}

public class ActionLogStartupBoundsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-log-").FullName;

    [Fact]
    public void MissingFile_HasNoBounds()
    {
        var (first, last) = ActionLogReader.StartupChangeBoundsUtc(
            Path.Combine(_root, "absent.jsonl"));
        Assert.Null(first);
        Assert.Null(last);
    }

    /// Only lines carrying a "startup" field anchor the trend — a clean
    /// line does not change what starts with Windows, so it must not reset
    /// the after-window every time a cache is emptied.
    [Fact]
    public void ReadsFirstAndLastStartupLine_IgnoringCleanLines()
    {
        var path = Path.Combine(_root, "log.jsonl");
        File.WriteAllLines(path, new[]
        {
            @"{""ts"":""2026-08-20T10:00:00Z"",""startup"":""Spotify"",""hive"":""HKCU"",""enabled"":false}",
            @"{""ts"":""2026-08-25T10:00:00Z"",""targetId"":""user-temp"",""path"":""C:\\x"",""bytes"":10,""action"":""recycled""}",
            @"{""ts"":""2026-08-22T10:00:00Z"",""startup"":""Teams"",""hive"":""HKCU"",""enabled"":false}",
            "not json at all",
        });

        var (first, last) = ActionLogReader.StartupChangeBoundsUtc(path);

        Assert.Equal(new DateTime(2026, 8, 20, 10, 0, 0, DateTimeKind.Utc), first);
        Assert.Equal(new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc), last);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
