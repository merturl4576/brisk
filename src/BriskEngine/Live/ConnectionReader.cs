using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BriskEngine.Live;

public sealed record ConnectionRow(string App, string Owner, string? Host, int Count,
    bool IsTelemetry, bool IsLocal);

public sealed record ConnectionSnapshot(IReadOnlyList<ConnectionRow> Rows, bool DnsRead)
{
    /// null when the DNS cache was not read: without host names telemetry
    /// cannot be told apart, so the count is unknown, not zero.
    public int? TelemetryCount => DnsRead ? Rows.Where(r => r.IsTelemetry).Sum(r => r.Count) : null;
}

public sealed class ConnectionReader
{
    public const string UnknownOwner = "Unknown";
    public const string LocalOwner = "Local network";

    private readonly ITcpTableProbe _tcp;
    private readonly IDnsCacheProbe _dns;
    private readonly OwnerTable _owners;
    private readonly IProcessSnapshotProbe _processes;
    private readonly IAppNamer _namer;

    public ConnectionReader(ITcpTableProbe tcp, IDnsCacheProbe dns, OwnerTable owners,
        IProcessSnapshotProbe processes, IAppNamer namer)
    {
        _tcp = tcp;
        _dns = dns;
        _owners = owners;
        _processes = processes;
        _namer = namer;
    }

    private static IPAddress Normal(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public ConnectionSnapshot Read()
    {
        var dns = _dns.Entries();
        var hosts = new Dictionary<IPAddress, string>();
        if (dns is not null)
            foreach (var (name, ip) in dns) hosts.TryAdd(Normal(ip), name);
        var processes = _processes.Snapshot().GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());

        var rows = _tcp.Established()
            .Where(c => !IPAddress.IsLoopback(Normal(c.Remote)))
            .Select(c =>
            {
                var remote = Normal(c.Remote);
                var app = processes.TryGetValue(c.Pid, out var p) ? _namer.Name(p.Name, p.ExePath) : $"PID {c.Pid}";
                var local = OwnerTable.IsLocal(remote);
                hosts.TryGetValue(remote, out var host);
                var owner = local ? LocalOwner
                    : (host is null ? null : OwnerTable.OwnerOfHost(host))
                      ?? _owners.OwnerOfAddress(remote) ?? UnknownOwner;
                return (App: app, Owner: owner, Host: host, Telemetry: TelemetryHosts.IsTelemetry(host), Local: local);
            })
            .GroupBy(x => (x.App, x.Owner, x.Telemetry, x.Local))
            .Select(g => new ConnectionRow(g.Key.App, g.Key.Owner,
                g.Select(x => x.Host).FirstOrDefault(h => h is not null),
                g.Count(), g.Key.Telemetry, g.Key.Local))
            .OrderByDescending(r => r.IsTelemetry)
            .ThenByDescending(r => r.Count)
            .ToList();

        return new ConnectionSnapshot(rows, dns is not null);
    }
}
