using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace BriskEngine.Live;

/// Who owns an address: a curated list of domain suffixes for host names, and
/// a bundled table of IP ranges (iptoasn.com, public domain) for addresses
/// with no name. No lookup ever leaves the machine.
public sealed class OwnerTable
{
    private static readonly (string Suffix, string Owner)[] Domains =
    {
        ("microsoft.com", "Microsoft"), ("msft.net", "Microsoft"), ("live.com", "Microsoft"),
        ("live.net", "Microsoft"), ("windows.com", "Microsoft"), ("windows.net", "Microsoft"),
        ("windowsupdate.com", "Microsoft"), ("msn.com", "Microsoft"), ("bing.com", "Microsoft"),
        ("bing.net", "Microsoft"), ("office.com", "Microsoft"), ("office.net", "Microsoft"),
        ("office365.com", "Microsoft"), ("outlook.com", "Microsoft"), ("skype.com", "Microsoft"),
        ("xbox.com", "Microsoft"), ("xboxlive.com", "Microsoft"), ("azure.com", "Microsoft"),
        ("azureedge.net", "Microsoft"), ("trafficmanager.net", "Microsoft"), ("msedge.net", "Microsoft"),
        ("microsoftonline.com", "Microsoft"), ("onedrive.com", "Microsoft"), ("sharepoint.com", "Microsoft"),
        ("github.com", "GitHub"), ("githubusercontent.com", "GitHub"),
        ("google.com", "Google"), ("googleapis.com", "Google"), ("gstatic.com", "Google"),
        ("googleusercontent.com", "Google"), ("googlevideo.com", "Google"), ("youtube.com", "Google"),
        ("ytimg.com", "Google"), ("doubleclick.net", "Google"), ("google-analytics.com", "Google"),
        ("gvt1.com", "Google"), ("gvt2.com", "Google"),
        ("amazonaws.com", "Amazon"), ("amazon.com", "Amazon"), ("cloudfront.net", "Amazon"),
        ("cloudflare.com", "Cloudflare"), ("cloudflare.net", "Cloudflare"), ("cloudflare-dns.com", "Cloudflare"),
        ("akamai.net", "Akamai"), ("akamaiedge.net", "Akamai"), ("akamaihd.net", "Akamai"),
        ("akamaized.net", "Akamai"), ("edgekey.net", "Akamai"), ("edgesuite.net", "Akamai"),
        ("facebook.com", "Meta"), ("facebook.net", "Meta"), ("fbcdn.net", "Meta"),
        ("instagram.com", "Meta"), ("whatsapp.net", "Meta"), ("whatsapp.com", "Meta"),
        ("apple.com", "Apple"), ("icloud.com", "Apple"), ("mzstatic.com", "Apple"),
        ("discord.com", "Discord"), ("discord.gg", "Discord"), ("discordapp.com", "Discord"),
        ("discordapp.net", "Discord"), ("discord.media", "Discord"),
        ("spotify.com", "Spotify"), ("scdn.co", "Spotify"), ("spotifycdn.com", "Spotify"),
        ("riotgames.com", "Riot Games"), ("riotcdn.net", "Riot Games"), ("leagueoflegends.com", "Riot Games"),
        ("steampowered.com", "Valve"), ("steamcommunity.com", "Valve"), ("steamserver.net", "Valve"),
        ("steamcontent.com", "Valve"), ("valvesoftware.com", "Valve"),
        ("anthropic.com", "Anthropic"), ("claude.ai", "Anthropic"),
        ("openai.com", "OpenAI"), ("chatgpt.com", "OpenAI"), ("oaiusercontent.com", "OpenAI"),
        ("epicgames.com", "Epic Games"), ("unrealengine.com", "Epic Games"),
        ("nvidia.com", "NVIDIA"), ("geforce.com", "NVIDIA"),
        ("adobe.com", "Adobe"), ("adobe.io", "Adobe"),
        ("dropbox.com", "Dropbox"), ("dropboxapi.com", "Dropbox"),
        ("zoom.us", "Zoom"), ("slack.com", "Slack"), ("slack-edge.com", "Slack"),
        ("telegram.org", "Telegram"), ("twitch.tv", "Twitch"),
        ("x.com", "X"), ("twitter.com", "X"), ("twimg.com", "X"),
        ("tiktok.com", "ByteDance"), ("tiktokcdn.com", "ByteDance"), ("byteoversea.com", "ByteDance"),
        ("docker.com", "Docker"), ("docker.io", "Docker"), ("npmjs.org", "npm"), ("npmjs.com", "npm"),
    };

    private readonly UInt128[] _starts4, _ends4, _starts6, _ends6;
    private readonly string[] _owners4, _owners6;

    public OwnerTable(string tsv)
    {
        var v4 = new List<(UInt128 Start, UInt128 End, string Owner)>();
        var v6 = new List<(UInt128 Start, UInt128 End, string Owner)>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('\t');
            if (f.Length < 3 || !IPAddress.TryParse(f[0], out var a) || !IPAddress.TryParse(f[1], out var b)) continue;
            var start = ToNumber(a, out var isV6);
            var end = ToNumber(b, out _);
            (isV6 ? v6 : v4).Add((start, end, f[2]));
        }
        v4.Sort((x, y) => x.Start.CompareTo(y.Start));
        v6.Sort((x, y) => x.Start.CompareTo(y.Start));
        _starts4 = v4.Select(x => x.Start).ToArray(); _ends4 = v4.Select(x => x.End).ToArray(); _owners4 = v4.Select(x => x.Owner).ToArray();
        _starts6 = v6.Select(x => x.Start).ToArray(); _ends6 = v6.Select(x => x.End).ToArray(); _owners6 = v6.Select(x => x.Owner).ToArray();
    }

    private static readonly Lazy<OwnerTable> DefaultTable = new(() => new OwnerTable(LoadResource()));
    public static OwnerTable Default => DefaultTable.Value;

    private static string LoadResource()
    {
        using var stream = typeof(OwnerTable).Assembly.GetManifestResourceStream("BriskEngine.Live.asn-owners.tsv");
        if (stream is null) return "";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string? OwnerOfHost(string host)
    {
        var h = host.TrimEnd('.').ToLowerInvariant();
        foreach (var (suffix, owner) in Domains)
            if (h == suffix || h.EndsWith("." + suffix, StringComparison.Ordinal)) return owner;
        return null;
    }

    public string? OwnerOfAddress(IPAddress ip)
    {
        var key = ToNumber(ip, out var isV6);
        return isV6 ? Find(_starts6, _ends6, _owners6, key) : Find(_starts4, _ends4, _owners4, key);
    }

    private static string? Find(UInt128[] starts, UInt128[] ends, string[] owners, UInt128 key)
    {
        var i = Array.BinarySearch(starts, key);
        if (i < 0) i = ~i - 1;
        return i >= 0 && key <= ends[i] ? owners[i] : null;
    }

    internal static UInt128 ToNumber(IPAddress ip, out bool isV6)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        isV6 = bytes.Length == 16;
        UInt128 n = 0;
        foreach (var b in bytes) n = (n << 8) | b;
        return n;
    }

    public static bool IsLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal;
    }
}

public static class TelemetryHosts
{
    private static readonly string[] Suffixes =
    {
        "events.data.microsoft.com", "telemetry.microsoft.com", "vortex.data.microsoft.com",
        "vortex-win.data.microsoft.com", "settings-win.data.microsoft.com", "watson.microsoft.com",
        "events.data.msn.com",
    };

    public static bool IsTelemetry(string? host)
    {
        if (host is null) return false;
        var h = host.TrimEnd('.').ToLowerInvariant();
        return Suffixes.Any(s => h == s || h.EndsWith("." + s, StringComparison.Ordinal));
    }
}
