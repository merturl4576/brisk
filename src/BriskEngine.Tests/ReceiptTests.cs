using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ReceiptTests : IDisposable
{
    private static readonly DateTime Run = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-receipt-").FullName;

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static BootRecord Boot(DateTime when, int seconds) =>
        new(when, seconds * 1000, null, Array.Empty<BootOffender>());

    private static Receipt Finished(params StartupRam[] ram) => new("r1", Run, Run.AddMinutes(1),
        100, 300, 0, 14, 3, ram, Array.Empty<string>(), false, 48_000, null, 9, 1, 3, null);

    [Fact]
    public void MedianBootMs_UsesUpToFiveBootsBeforeTheRun()
    {
        var boots = new[]
        {
            Boot(Run.AddDays(-1), 40), Boot(Run.AddDays(-2), 60), Boot(Run.AddDays(-3), 50),
            Boot(Run.AddHours(2), 20),   // after the run: ignored
        };
        Assert.Equal(50_000, ReceiptCalculator.MedianBootMs(boots, Run));
        Assert.Null(ReceiptCalculator.MedianBootMs(Array.Empty<BootRecord>(), Run));
    }

    [Fact]
    public void Complete_TakesTheFirstBootAfterTheRun()
    {
        var boots = new[] { Boot(Run.AddHours(5), 33), Boot(Run.AddHours(2), 31) };
        var r = ReceiptCalculator.Complete(Finished(), boots, Array.Empty<ProcSample>(), null, Run.AddHours(1));
        Assert.Null(r.BootMsAfter);   // "now" is before either boot
        r = ReceiptCalculator.Complete(Finished(), boots, Array.Empty<ProcSample>(), null, Run.AddHours(6));
        Assert.Equal(31_000, r.BootMsAfter);
    }

    [Fact]
    public void Complete_ConfirmsRamOnlyAfterTheBootHasSettled_AndNamesWhatStillStarted()
    {
        var ram = new[]
        {
            new StartupRam("startup:HKCU:Discord", "Discord", new[] { "Discord" }, 900),
            new StartupRam("startup:HKCU:Spotify", "Spotify", new[] { "Spotify" }, 300),
        };
        var boots = new[] { Boot(Run.AddHours(2), 31) };
        var running = new[] { new ProcSample(5, "Spotify", 1, null, 300, false, null) };

        var early = ReceiptCalculator.Complete(Finished(ram), boots, running, null, Run.AddHours(2).AddMinutes(1));
        Assert.False(early.StartupRamConfirmed);
        Assert.Null(early.RamFreedBytes);

        var settled = ReceiptCalculator.Complete(Finished(ram), boots, running, null, Run.AddHours(2).AddMinutes(4));
        Assert.True(settled.StartupRamConfirmed);
        Assert.Equal(new[] { "Spotify" }, settled.StillStarted);
        Assert.Equal(900, settled.RamFreedBytes);
    }

    [Fact]
    public void Complete_TelemetryAfterWaitsTwoMinutes()
    {
        var r = ReceiptCalculator.Complete(Finished(), Array.Empty<BootRecord>(), Array.Empty<ProcSample>(), 0, Run.AddMinutes(2));
        Assert.Null(r.TelemetryAfter);
        r = ReceiptCalculator.Complete(Finished(), Array.Empty<BootRecord>(), Array.Empty<ProcSample>(), 0, Run.AddMinutes(4));
        Assert.Equal(0, r.TelemetryAfter);
    }

    [Fact]
    public void FreedBytes_IsTheRealChangeInFreeSpace()
    {
        Assert.Equal(200, Finished().FreedBytes);
        Assert.Equal(0, (Finished() with { FreeBytesAfter = 50 }).FreedBytes);
    }

    [Fact]
    public void Recorder_BeforeAndAfter()
    {
        var ctx = TestContext.Empty();
        var disk = (FakeDisk)ctx.Disk;
        var reg = (FakeRegistry)ctx.Registry;
        reg.SetString(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", "Discord", @"C:\D\Update.exe --processStart Discord.exe");
        ((FakeEventLog)ctx.EventLog).Boots.Add(Boot(Run.AddDays(-1), 48));
        var startup = new StartupManager(reg, null);
        var procs = new FakeProcs { Now = new() { new ProcSample(9, "Discord", 1, null, 700, true, null) } };
        var store = new ReceiptStore(Path.Combine(_root, "receipts"));
        var recorder = new ReceiptRecorder(ctx, startup, procs, store, () => 3, () => Run);

        var discord = ActionPlanBuilderRow("startup:HKCU:Discord", "Discord");
        var plan = new ActionPlan(Run, new[] { new ActionGroup(ActionGroupKind.Speed, new[] { discord }) },
            new BriskEngine.Models.ScanResult(Array.Empty<BriskEngine.Models.TargetScanResult>()));
        disk.Free = 1_000;
        recorder.Before("r1", plan, ActionSelection.Default(plan));

        startup.SetEnabled("HKCU", "Discord", false);
        disk.Free = 21_000;
        recorder.After(new RunRecord("r1", Run, Run.AddMinutes(1), new[]
        {
            new RowResult("startup:HKCU:Discord", RowOutcome.Done, null, ReadBackState.Held, 0, 0),
            new RowResult("privacy.ads", RowOutcome.Done, null, ReadBackState.Held, 0, 0),
            new RowResult("privacy.error-reports", RowOutcome.Done, null, ReadBackState.WrittenButIgnored, 0, 0),
        }, Array.Empty<string>()));

        var r = store.Load("r1")!;
        Assert.Equal(20_000, r.FreedBytes);
        Assert.Equal(1, r.StartupEnabledBefore);
        Assert.Equal(0, r.StartupEnabledAfter);
        Assert.Equal(700, Assert.Single(r.StartupRam).PrivateBytes);
        Assert.Equal(48_000, r.BootMsBefore);
        Assert.Equal(1, r.PrivacyOff);       // the ignored row is its own number, not "off"
        Assert.Equal(1, r.PrivacyIgnored);
        Assert.Equal(3, r.TelemetryBefore);
        Assert.Equal("r1", store.Latest()!.RunId);
    }

    [Fact]
    public void Recorder_PrivacyOff_LeavesOutARowThatTurnedBackOn()
    {
        var ctx = TestContext.Empty();
        var store = new ReceiptStore(Path.Combine(_root, "receipts"));
        var recorder = new ReceiptRecorder(ctx, new StartupManager((FakeRegistry)ctx.Registry, null),
            new FakeProcs(), store, () => null, () => Run);
        var plan = new ActionPlan(Run, Array.Empty<ActionGroup>(),
            new BriskEngine.Models.ScanResult(Array.Empty<BriskEngine.Models.TargetScanResult>()));
        recorder.Before("r2", plan, ActionSelection.Default(plan));
        recorder.After(new RunRecord("r2", Run, Run.AddMinutes(1), new[]
        {
            new RowResult("privacy.ads", RowOutcome.Done, null, ReadBackState.Held, 0, 0),
            new RowResult("privacy.copilot", RowOutcome.Done, null, ReadBackState.Reverted, 0, 0),
            new RowResult("privacy.uac", RowOutcome.PendingRestart, null, null, 0, 0),
        }, Array.Empty<string>()));

        Assert.Equal(2, store.Load("r2")!.PrivacyOff);
    }

    private static ActionItem ActionPlanBuilderRow(string id, string title) => new(id, ActionGroupKind.Speed,
        null, title, null, null, null, null, null, true, false, false, true, ActionLinkKind.Rules,
        new[] { id }, Array.Empty<ActionFile>());
}
