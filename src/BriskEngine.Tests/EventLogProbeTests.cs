using System;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public class EventLogProbeTests
{
    [Fact]
    public void FakeEventLog_ReturnsWhatItWasGiven()
    {
        var log = new FakeEventLog();
        log.Boots.Add(new BootRecord(new DateTime(2026, 8, 18), 51237, 24437));
        log.Offenders.Add(new BootOffender(new DateTime(2026, 8, 18), "Spotify.exe",
            "Spotify", @"C:\x\Spotify.exe", 37141));

        Assert.Equal(51237, log.RecentBoots(5)[0].BootMs);
        Assert.Equal(37141, log.RecentOffenders(5)[0].DegradationMs);
    }

    [Fact]
    public void EmptyContext_HasNoBootHistory()
    {
        Assert.Empty(TestContext.Empty().EventLog.RecentBoots(5));
        Assert.Empty(TestContext.Empty().EventLog.RecentOffenders(5));
    }
}
