using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

public sealed class DeferredCleanQueueTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-defer-").FullName;
    private string QueuePath => Path.Combine(_root, "deferred.json");
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } }

    private static TargetScanResult Scan(CleanupTarget t, string? skipped = null) =>
        new(t, new List<ResolvedItem>(), skipped);

    [Fact]
    public void Add_IsPersisted_AndDeduplicated()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        q.Add("chrome-cache", Now.AddMinutes(1));
        q.Add("whatsapp-cache", Now);

        var again = new DeferredCleanQueue(QueuePath);
        Assert.Equal(2, again.Pending().Count);
    }

    [Fact]
    public void ProcessDue_CleansWhenTheAppIsClosed_AndKeepsItWhileOpen()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        q.Add("whatsapp-cache", Now);
        var apps = new FakeRunningApps();
        apps.Running.Add("WhatsApp.Root");
        var cleaned = new List<string>();

        var reports = q.ProcessDue(Now.AddMinutes(5), apps, t => Scan(t),
            s => { cleaned.Add(s.Target.Id); return new CleanReport(new List<CleanEntry>()); });

        Assert.Single(reports);
        Assert.Equal(new[] { "chrome-cache" }, cleaned);
        var left = Assert.Single(q.Pending());
        Assert.Equal("whatsapp-cache", left.TargetId);
    }

    [Fact]
    public void ProcessDue_DropsWorkOlderThanAWeek()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("chrome-cache", Now);
        var reports = q.ProcessDue(Now.AddDays(8), new FakeRunningApps(), t => Scan(t),
            _ => throw new InvalidOperationException("must not clean expired work"));
        Assert.Empty(reports);
        Assert.Empty(q.Pending());
    }

    [Fact]
    public void ProcessDue_KeepsATargetTheFreshScanStillSkips()
    {
        var q = new DeferredCleanQueue(QueuePath);
        q.Add("discord-cache", Now);
        q.ProcessDue(Now, new FakeRunningApps(), t => Scan(t, "Discord is running"),
            _ => throw new InvalidOperationException("must not clean a skipped scan"));
        Assert.Single(q.Pending());
    }

    [Fact]
    public void Pending_ACorruptFileReadsAsEmpty()
    {
        File.WriteAllText(QueuePath, "{not json");
        Assert.Empty(new DeferredCleanQueue(QueuePath).Pending());
    }
}
