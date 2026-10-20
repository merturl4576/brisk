using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeUsage : INetworkUsageProbe
{
    public List<(string Id, long Sent, long Received)>? Rows = new();
    public IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days) => Rows;
}

public sealed class NetworkUsageReaderTests
{
    [Fact]
    public void Parse_AnArray()
    {
        var rows = RealNetworkUsageProbe.Parse("""[{"id":"a","sent":10,"recv":20},{"id":"","sent":1.0E10,"recv":0}]""")!;
        Assert.Equal(2, rows.Count);
        Assert.Equal(("a", 10L, 20L), rows[0]);
        Assert.Equal(10_000_000_000L, rows[1].Sent);
    }

    [Fact]
    public void Parse_ASingleObject_AnEmptyOutput_AndGarbage()
    {
        Assert.Single(RealNetworkUsageProbe.Parse("""{"id":null,"sent":5,"recv":6}""")!);
        Assert.Empty(RealNetworkUsageProbe.Parse("  ")!);
        Assert.Null(RealNetworkUsageProbe.Parse("Add-Type : something failed"));
    }

    [Fact]
    public void RealProbe_SendsAnEncodedScriptForTheRequestedDays()
    {
        string? args = null;
        var probe = new RealNetworkUsageProbe(new ScriptedRunner((_, a) => { args = a; return (0, "[]"); }));

        Assert.Empty(probe.Usage(30)!);

        var encoded = args!.Split(' ').Last();
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains("AddDays(-30)", script);
        Assert.DoesNotContain("$profile", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-EncodedCommand", args);
    }

    [Fact]
    public void RealProbe_AFailingRunIsNotRead()
    {
        var probe = new RealNetworkUsageProbe(new ScriptedRunner((_, _) => (1, "")));
        Assert.Null(probe.Usage(30));
    }

    [Fact]
    public void Top_NamesAppsMergesVersionsAndSortsBySent()
    {
        var usage = new FakeUsage
        {
            Rows = new()
            {
                (@"\device\harddiskvolume3\users\x\appdata\local\discord\app-1.0.9260\discord.exe", 100, 1),
                (@"\device\harddiskvolume3\users\x\appdata\local\discord\app-1.0.9261\discord.exe", 50, 1),
                ("5319275A.WhatsAppDesktop_cv1g1gvanyjgm", 500, 9),
                ("", 1_000, 2),
            },
        };

        var rows = new NetworkUsageReader(usage, new FakeNamer()).Top()!;

        Assert.Equal(new[] { NetworkUsageReader.WindowsSystem, "WhatsAppDesktop", "Discord" }, rows.Select(r => r.App));
        Assert.Equal(150, rows.Single(r => r.App == "Discord").BytesSent);
    }

    [Fact]
    public void Top_TakesTheLimit_AndPassesUnreadThrough()
    {
        var usage = new FakeUsage { Rows = Enumerable.Range(1, 15).Select(i => ($"app{i}", (long)i, 0L)).ToList() };
        Assert.Equal(10, new NetworkUsageReader(usage, new FakeNamer()).Top()!.Count);
        Assert.Null(new NetworkUsageReader(new FakeUsage { Rows = null }, new FakeNamer()).Top());
    }
}
