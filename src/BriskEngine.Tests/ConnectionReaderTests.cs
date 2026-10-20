using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

sealed class FakeTcp : ITcpTableProbe
{
    public List<TcpConnection> Rows = new();
    public IReadOnlyList<TcpConnection> Established() => Rows;
}

sealed class FakeDns : IDnsCacheProbe
{
    public List<(string Name, IPAddress Address)>? Rows = new();
    public IReadOnlyList<(string Name, IPAddress Address)>? Entries() => Rows;
}

public sealed class ConnectionReaderTests
{
    private const string Tsv = "# test table\n1.0.0.0\t1.0.0.255\tCloudflare\n8.8.8.0\t8.8.8.255\tGoogle\n2001:4860::\t2001:4860:ffff:ffff:ffff:ffff:ffff:ffff\tGoogle\n";

    private static TcpConnection C(int pid, string ip) => new(pid, IPAddress.Parse(ip), 443);

    private static (ConnectionReader Reader, FakeTcp Tcp, FakeDns Dns) Make()
    {
        var tcp = new FakeTcp();
        var dns = new FakeDns();
        var procs = new FakeProcs
        {
            Now = new()
            {
                new ProcSample(10, "StartMenuExperienceHost", 1, null, 0, false, null),
                new ProcSample(20, "chrome", 1, null, 0, true, null),
                new ProcSample(30, "svchost", 0, null, 0, false, null),
            },
        };
        return (new ConnectionReader(tcp, dns, new OwnerTable(Tsv), procs, new FakeNamer()), tcp, dns);
    }

    [Theory]
    [InlineData("8.8.8.8", "Google")]
    [InlineData("1.0.0.1", "Cloudflare")]
    [InlineData("2001:4860:4860::8888", "Google")]
    [InlineData("9.9.9.9", null)]
    public void OwnerOfAddress(string ip, string? owner) =>
        Assert.Equal(owner, new OwnerTable(Tsv).OwnerOfAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("www.bing.com", "Microsoft")]
    [InlineData("glb.cws.prod.dcat.dsp.trafficmanager.net", "Microsoft")]
    [InlineData("api.anthropic.com.", "Anthropic")]
    [InlineData("rr3---sn-4g5e6nsz.googlevideo.com", "Google")]
    [InlineData("example.org", null)]
    public void OwnerOfHost(string host, string? owner) => Assert.Equal(owner, OwnerTable.OwnerOfHost(host));

    [Theory]
    [InlineData("v10.events.data.microsoft.com", true)]
    [InlineData("v10.vortex-win.data.microsoft.com", true)]
    [InlineData("settings-win.data.microsoft.com", true)]
    [InlineData("www.microsoft.com", false)]
    [InlineData(null, false)]
    public void IsTelemetry(string? host, bool telemetry) => Assert.Equal(telemetry, TelemetryHosts.IsTelemetry(host));

    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.20.0.1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("8.8.8.8", false)]
    public void IsLocal(string ip, bool local) => Assert.Equal(local, OwnerTable.IsLocal(IPAddress.Parse(ip)));

    [Fact]
    public void Read_NamesTheAppAndTheOwner_AndCountsConnections()
    {
        var (reader, tcp, dns) = Make();
        tcp.Rows.AddRange(new[] { C(10, "13.107.5.80"), C(10, "13.107.5.81"), C(20, "8.8.8.8") });
        dns.Rows!.Add(("www.bing.com", IPAddress.Parse("13.107.5.80")));
        dns.Rows.Add(("www.bing.com", IPAddress.Parse("13.107.5.81")));

        var snap = reader.Read();

        Assert.True(snap.DnsRead);
        var start = snap.Rows.Single(r => r.App == "StartMenuExperienceHost");
        Assert.Equal("Microsoft", start.Owner);
        Assert.Equal("www.bing.com", start.Host);
        Assert.Equal(2, start.Count);
        Assert.Equal("Google", snap.Rows.Single(r => r.App == "Chrome").Owner);
        Assert.Equal(0, snap.TelemetryCount);
    }

    [Fact]
    public void Read_TagsTelemetry_AndSortsItFirst()
    {
        var (reader, tcp, dns) = Make();
        tcp.Rows.AddRange(new[] { C(20, "8.8.8.8"), C(30, "20.42.65.92") });
        dns.Rows!.Add(("v10.events.data.microsoft.com", IPAddress.Parse("20.42.65.92")));

        var snap = reader.Read();

        Assert.True(snap.Rows[0].IsTelemetry);
        Assert.Equal(1, snap.TelemetryCount);
    }

    [Fact]
    public void Read_WithoutTheDnsCache_TelemetryIsUnknown_AndTheTableStillNamesOwners()
    {
        var (reader, tcp, dns) = Make();
        dns.Rows = null;
        tcp.Rows.Add(C(20, "8.8.8.8"));

        var snap = reader.Read();

        Assert.False(snap.DnsRead);
        Assert.Null(snap.TelemetryCount);
        Assert.Equal("Google", Assert.Single(snap.Rows).Owner);
    }

    [Fact]
    public void Read_LocalAndUnknown()
    {
        var (reader, tcp, _) = Make();
        tcp.Rows.AddRange(new[] { C(20, "192.168.1.1"), C(20, "9.9.9.9"), C(20, "127.0.0.1") });
        var rows = reader.Read().Rows;
        Assert.Contains(rows, r => r.IsLocal && r.Owner == ConnectionReader.LocalOwner);
        Assert.Contains(rows, r => r.Owner == ConnectionReader.UnknownOwner);
        Assert.Equal(2, rows.Sum(r => r.Count));   // loopback left out
    }

    [Fact]
    public void DefaultTable_IsBundled_AndKnowsTheBigNames()
    {
        Assert.Equal("Google", OwnerTable.Default.OwnerOfAddress(IPAddress.Parse("8.8.8.8")));
        Assert.Equal("Microsoft", OwnerTable.Default.OwnerOfAddress(IPAddress.Parse("13.107.42.14")));
    }
}
