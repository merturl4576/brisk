using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeProcs : IProcessSnapshotProbe
{
    public Queue<List<ProcSample>> Sequence = new();
    public List<ProcSample> Now = new();
    public int ProcessorCount { get; set; } = 4;
    public IReadOnlyList<ProcSample> Snapshot() => Sequence.Count > 0 ? Sequence.Dequeue() : Now;
}

sealed class FakeNamer : IAppNamer
{
    public string Name(string processOrExeName, string? fullPath) =>
        RealAppNamer.Pretty(RealAppNamer.Stem(processOrExeName));
}

public sealed class LoadSamplerTests
{
    private static ProcSample P(int pid, string name, double cpuSeconds, long mb, int session = 1, bool window = false) =>
        new(pid, name, session, TimeSpan.FromSeconds(cpuSeconds), mb << 20, window, null);

    private static IReadOnlyList<AppLoad> Sample(List<ProcSample> before, List<ProcSample> after, int cores = 4)
    {
        var procs = new FakeProcs { ProcessorCount = cores };
        procs.Sequence.Enqueue(before);
        procs.Sequence.Enqueue(after);
        return new LoadSampler(procs, new FakeNamer(), _ => { }).Sample();
    }

    [Fact]
    public void GroupsAnAppsProcesses_AndMeasuresCpuOverTheWindow()
    {
        // 4 cores x 10 s = 40 s of capacity; Chrome used 8 s across two processes = 20 %
        var rows = Sample(
            new() { P(1, "chrome", 10, 300), P(2, "chrome", 5, 300) },
            new() { P(1, "chrome", 15, 400, window: true), P(2, "chrome", 8, 400) });

        var chrome = Assert.Single(rows);
        Assert.Equal("Chrome", chrome.DisplayName);
        Assert.Equal(20, chrome.CpuPercent);
        Assert.Equal(800L << 20, chrome.PrivateBytes);
        Assert.Equal(new[] { 1, 2 }, chrome.Pids);
        Assert.True(LoadSampler.CanClose(chrome));
    }

    [Fact]
    public void KeepsOnlyRowsAboveAThreshold_AndAtMostFive()
    {
        var before = Enumerable.Range(1, 8).Select(i => P(i, $"app{i}", 0, 600)).ToList();
        before.Add(P(50, "quiet", 0, 10));
        var after = Enumerable.Range(1, 8).Select(i => P(i, $"app{i}", i, 600)).ToList();
        after.Add(P(50, "quiet", 0, 10));

        var rows = Sample(before, after);

        Assert.Equal(LoadSampler.MaxRows, rows.Count);
        Assert.DoesNotContain(rows, r => r.Key == "quiet");
        Assert.Equal("app8", rows[0].Key);
    }

    [Fact]
    public void WindowsOwnProcessesGetANote_AndNoClose()
    {
        var rows = Sample(
            new() { P(10, "MsMpEng", 0, 200, session: 0) },
            new() { P(10, "MsMpEng", 8, 200, session: 0) });
        var defender = Assert.Single(rows);
        Assert.True(defender.IsSystem);
        Assert.Equal("action.load.note.defender", defender.NoteKey);
        Assert.False(LoadSampler.CanClose(defender));
    }

    [Fact]
    public void IgnoresIdleAndBrisk()
    {
        var rows = Sample(
            new() { P(0, "Idle", 0, 0, session: 0), P(7, "brisk-app", 0, 900) },
            new() { P(0, "Idle", 30, 0, session: 0), P(7, "brisk-app", 9, 900) });
        Assert.Empty(rows);
    }

    [Fact]
    public void AProcessThatStartedDuringTheWindow_CountsOnlyItsMemory()
    {
        var rows = Sample(new(), new() { P(3, "game", 50, 2000) });
        var game = Assert.Single(rows);
        Assert.Equal(0, game.CpuPercent);
    }

    [Theory]
    [InlineData("Discord", @"C:\Users\x\AppData\Local\Discord\Update.exe --processStart Discord.exe", "Discord", true)]
    [InlineData("Spotify", @"C:\Spotify\Spotify.exe /minimized", "Spotify", true)]
    [InlineData("Wispr Flow.lnk", @"C:\Users\x\Startup\Wispr Flow.lnk", "Wispr Flow", true)]
    [InlineData("Steam", @"C:\Steam\steam.exe -silent", "chrome", false)]
    public void StartupProcessMatch_FindsTheProcessAnEntryStarts(string name, string command, string process, bool match) =>
        Assert.Equal(match, StartupProcessMatch.Matches(new StartupEntry("HKCU", name, true, false, command), process));

    [Fact]
    public void StartupIdFor_PointsAtTheEntryThatStartsTheApp()
    {
        var load = new AppLoad("Discord", "Discord", 12, 1L << 30, false, null, null, new[] { 1 }, true);
        var entries = new[]
        {
            new StartupEntry("HKCU", "Discord", true, false, @"C:\Discord\Update.exe --processStart Discord.exe"),
        };
        Assert.Equal("startup:HKCU:Discord", LoadSampler.StartupIdFor(load, entries));
        Assert.Null(LoadSampler.StartupIdFor(load with { Key = "chrome" }, entries));
    }

    [Fact]
    public void Closer_AMissingProcessIsNotAnError()
    {
        var closer = new RealProcessCloser();
        Assert.Equal(0, closer.RequestClose(new[] { int.MaxValue }));
        closer.Kill(new[] { int.MaxValue });
    }

    [Theory]
    [InlineData("discord.exe", "Discord")]
    [InlineData("Code", "Code")]
    [InlineData("", "")]
    public void NamerFallback(string name, string expected) =>
        Assert.Equal(expected, RealAppNamer.Pretty(RealAppNamer.Stem(name)));

    [Fact]
    public void DevicePath_LeavesADosPathAlone() =>
        Assert.Equal(@"C:\x\y.exe", DevicePath.ToDos(@"C:\x\y.exe"));
}
