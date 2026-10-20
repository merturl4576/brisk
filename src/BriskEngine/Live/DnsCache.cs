using System;
using System.Collections.Generic;
using System.Management;
using System.Net;

namespace BriskEngine.Live;

public interface IDnsCacheProbe
{
    /// null: the cache was not read.
    IReadOnlyList<(string Name, IPAddress Address)>? Entries();
}

/// Without elevation the cache reads as EMPTY, not as refused (build 26200,
/// measured 2026-10-16) — so an unelevated read is reported as not read.
public sealed class RealDnsCacheProbe : IDnsCacheProbe
{
    private readonly Func<bool> _isElevated;

    public RealDnsCacheProbe(Func<bool> isElevated) { _isElevated = isElevated; }

    public IReadOnlyList<(string Name, IPAddress Address)>? Entries()
    {
        if (!_isElevated()) return null;
        try
        {
            var list = new List<(string Name, IPAddress Address)>();
            using var searcher = new ManagementObjectSearcher(
                new ManagementScope(@"\\.\root\StandardCimv2"),
                new ObjectQuery("SELECT Entry, Data, Type FROM MSFT_DNSClientCache"));
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    var type = Convert.ToInt32(o["Type"]);
                    if (type is not (1 or 28)) continue;   // A, AAAA
                    if (o["Entry"] is string name && o["Data"] is string data
                        && IPAddress.TryParse(data, out var ip))
                        list.Add((name, ip));
                }
            }
            return list;
        }
        catch (Exception) { return null; }
    }
}
