using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Xml.Linq;

namespace BriskEngine.Diagnostics.RealProbes;

/// Reads the boot performance channel Windows writes for itself. Windows timed
/// its own boot and named the programs that delayed it, which is why these
/// numbers beat anything brisk could infer from the outside.
///
/// Two things shape this class. The channel is admin-only, so an ordinary
/// `brisk scan` hits UnauthorizedAccessException and must come back with an
/// empty history rather than an exception — same contract as RealSensorProbe.
/// And values are pulled out of the payload by field name, never by position:
/// the ID 100 payload alone carries 40-odd Data elements whose order is not
/// contractual, and an index-based read would keep compiling while silently
/// reporting the wrong millisecond count.
public sealed class RealEventLogProbe : IEventLogProbe
{
    private const string ChannelName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    /// Windows logs one of these per boot, with the totals it measured.
    private const int BootPerformanceEventId = 100;

    /// ...and one of these per program it decided to blame for that boot.
    private const int BootDegradationEventId = 101;

    private static readonly XNamespace EventNs =
        "http://schemas.microsoft.com/win/2004/08/events/event";

    public IReadOnlyList<BootRecord> RecentBoots(int count) =>
        Read(BootPerformanceEventId, count, static (when, fields) =>
            Int(fields, "BootTime") is int bootMs
                ? new BootRecord(when, bootMs, Int(fields, "MainPathBootTime") ?? 0)
                : null);

    public IReadOnlyList<BootOffender> RecentOffenders(int count) =>
        Read(BootDegradationEventId, count, static (when, fields) =>
        {
            var name = Text(fields, "Name");
            // A blamed program with no name and no measured delay is not
            // something a user can act on, so it is not worth reporting.
            if (name.Length == 0 || Int(fields, "DegradationTime") is not int degradationMs)
                return null;
            return new BootOffender(
                when, name, Text(fields, "FriendlyName"), Text(fields, "Path"), degradationMs);
        });

    private static IReadOnlyList<T> Read<T>(
        int eventId, int count, Func<DateTime, IReadOnlyDictionary<string, string>, T?> parse)
        where T : class
    {
        var found = new List<T>();
        if (count <= 0) return found;
        try
        {
            var query = new EventLogQuery(
                ChannelName, PathType.LogName, $"*[System[(EventID={eventId})]]")
            {
                ReverseDirection = true,   // newest boot first — "recent" is the whole point
            };
            using var reader = new EventLogReader(query);
            while (found.Count < count)
            {
                using var record = reader.ReadEvent();
                if (record is null) break;   // no more events in the channel
                var parsed = parse(record.TimeCreated ?? DateTime.MinValue, Fields(record.ToXml()));
                if (parsed is not null) found.Add(parsed);
            }
        }
        catch (Exception)
        {
            // No elevation (UnauthorizedAccessException), no such channel on this
            // edition of Windows, or a record that would not render. A probe never
            // lets its own failure reach a rule: an absent boot history reads as
            // "we have nothing to say", which is true, and stays true.
            return found;
        }
        return found;
    }

    /// EventData/Data elements keyed by their Name attribute. Names repeat in a
    /// well-formed payload only if Microsoft ships a duplicate, so the last one
    /// wins rather than throwing.
    private static IReadOnlyDictionary<string, string> Fields(string xml)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var root = XDocument.Parse(xml).Root;
        if (root is null) return fields;
        foreach (var data in root.Elements(EventNs + "EventData").Elements(EventNs + "Data"))
        {
            var name = data.Attribute("Name")?.Value;
            if (name is null) continue;
            fields[name] = data.Value;
        }
        return fields;
    }

    private static int? Int(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static string Text(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var raw) ? raw : string.Empty;
}
