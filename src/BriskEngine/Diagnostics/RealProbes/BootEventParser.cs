using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace BriskEngine.Diagnostics.RealProbes;

/// One ID 100 record, before its offenders are attached.
internal sealed record ParsedBoot(DateTime Started, int BootMs, int? MainPathMs);

/// One ID 101 record, carrying the boot it belongs to.
internal sealed record ParsedOffender(DateTime BootStarted, BootOffender Offender);

/// Turns the raw event XML of the boot performance channel into brisk's own
/// types. Split out of RealEventLogProbe so it can be tested without the
/// channel: these are pure string-to-value functions, and the machine running
/// the tests needs neither the log nor elevation to prove they are right.
///
/// Everything is read by field name. The ID 100 payload alone carries 44 Data
/// elements whose order Microsoft never promised, and index 4 of that payload
/// is SystemBootInstance — a boot *counter*, 392 on the machine this was
/// verified against, which an off-by-one would report as a 392 ms boot.
internal static class BootEventParser
{
    private static readonly XNamespace EventNs =
        "http://schemas.microsoft.com/win/2004/08/events/event";

    /// EventData/Data elements keyed by their Name attribute.
    internal static IReadOnlyDictionary<string, string> Fields(string xml)
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

    /// Null when this is not a boot we can honestly describe: no start time
    /// leaves it unplaceable in time and uncorrelatable to its offenders, and
    /// no BootTime leaves nothing to report.
    internal static ParsedBoot? ReadBoot(string xml)
    {
        var fields = Fields(xml);
        if (Time(fields, "BootStartTime") is not DateTime started) return null;
        if (Int(fields, "BootTime") is not int bootMs) return null;
        return new ParsedBoot(started, bootMs, Int(fields, "MainPathBootTime"));
    }

    /// Null when the record cannot be attached to a boot (no StartTime), names
    /// no program, or carries no measured delay — each on its own makes the
    /// record useless to a caller, so any one of them drops it.
    internal static ParsedOffender? ReadOffender(string xml)
    {
        var fields = Fields(xml);
        if (Time(fields, "StartTime") is not DateTime bootStarted) return null;
        var name = Text(fields, "Name");
        if (name.Length == 0) return null;
        if (Int(fields, "DegradationTime") is not int degradationMs) return null;
        return new ParsedOffender(bootStarted, new BootOffender(
            name, Text(fields, "FriendlyName"), Text(fields, "Path"), degradationMs));
    }

    /// Attaches each offender to the boot it belongs to.
    ///
    /// The key is the boot's own start instant, which ID 100 calls BootStartTime
    /// and ID 101 calls StartTime. Those two are byte-identical across every
    /// cluster on the verified machine, whereas the records' own timestamps are
    /// not — an ID 101 is written a few hundred ticks after its ID 100, so
    /// grouping on TimeCreated would have split every boot into singletons.
    ///
    /// Boot order is the caller's (newest first). Offenders are ordered worst
    /// first, because "what slowed my boot" wants the worst named first, and
    /// the order they sit in the log carries no meaning at all.
    internal static IReadOnlyList<BootRecord> Assemble(
        IReadOnlyList<ParsedBoot> boots, IReadOnlyList<ParsedOffender> offenders)
    {
        var blamed = new Dictionary<DateTime, List<BootOffender>>();
        foreach (var offender in offenders)
        {
            if (!blamed.TryGetValue(offender.BootStarted, out var list))
                blamed[offender.BootStarted] = list = new List<BootOffender>();
            list.Add(offender.Offender);
        }

        var results = new List<BootRecord>(boots.Count);
        foreach (var boot in boots)
        {
            // An offender whose boot is not in this window is dropped rather
            // than attached to the nearest one: a wrong attribution is worse
            // than a missing one.
            IReadOnlyList<BootOffender> theirs = blamed.TryGetValue(boot.Started, out var list)
                ? list.OrderByDescending(o => o.DegradationMs).ToArray()
                : Array.Empty<BootOffender>();
            results.Add(new BootRecord(boot.Started, boot.BootMs, boot.MainPathMs, theirs));
        }
        return results;
    }

    private static int? Int(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// The channel writes ISO-8601 with a Z, e.g. 2026-08-16T22:28:43.1933117Z.
    /// RoundtripKind keeps that UTC and keeps all seven fractional digits, so
    /// two records naming the same boot parse to the same instant exactly —
    /// which is what makes it safe to use as a correlation key.
    private static DateTime? Time(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var raw)
        && DateTime.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;

    private static string Text(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var raw) ? raw : string.Empty;
}
