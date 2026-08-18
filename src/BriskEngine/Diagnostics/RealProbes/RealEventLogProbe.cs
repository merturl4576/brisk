using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;

namespace BriskEngine.Diagnostics.RealProbes;

/// Reads the boot performance channel Windows writes for itself. Windows timed
/// its own boot and named the programs that delayed it, which is why these
/// numbers beat anything brisk could infer from the outside.
///
/// The channel is admin-only, so an ordinary `brisk scan` hits
/// UnauthorizedAccessException and must come back with an empty history rather
/// than an exception — same contract as RealSensorProbe. Parsing lives in
/// BootEventParser so it can be tested without the log; this class is only the
/// reading and the failure handling.
public sealed class RealEventLogProbe : IEventLogProbe
{
    private const string ChannelName = "Microsoft-Windows-Diagnostics-Performance/Operational";

    /// Windows logs one of these per boot, with the totals it measured.
    private const int BootPerformanceEventId = 100;

    /// ...and one of these per program it decided to blame for that boot.
    private const int BootDegradationEventId = 101;

    public IReadOnlyList<BootRecord> RecentBoots(int count)
    {
        if (count <= 0) return Array.Empty<BootRecord>();
        var boots = ReadBoots(count);
        if (boots.Count == 0) return Array.Empty<BootRecord>();

        // Only the offenders that can belong to a boot we kept are worth
        // reading, and the oldest of those bounds the walk. Reading every ID 101
        // in the channel would mean scanning years of history to attach a
        // handful of names.
        var offenders = ReadOffendersBackTo(boots[boots.Count - 1].Started);
        return BootEventParser.Assemble(boots, offenders);
    }

    private static List<ParsedBoot> ReadBoots(int count)
    {
        var boots = new List<ParsedBoot>();
        EventLogReader reader;
        try
        {
            reader = OpenReader(BootPerformanceEventId);
        }
        catch (Exception)
        {
            // No elevation (UnauthorizedAccessException), or no such channel on
            // this edition of Windows. Nothing to say, which is true and stays true.
            return boots;
        }

        using (reader)
        {
            while (boots.Count < count)
            {
                var xml = NextRecordXml(reader);
                if (xml is null) break;
                ParsedBoot? parsed;
                try
                {
                    parsed = BootEventParser.ReadBoot(xml);
                }
                catch (Exception)
                {
                    // One record that will not parse must not hide every older
                    // boot behind it, so this skips rather than stopping.
                    continue;
                }
                if (parsed is not null) boots.Add(parsed);
            }
        }
        return boots;
    }

    /// Walks ID 101 newest-first and stops at the first record belonging to a
    /// boot older than `oldestBootStart`. The channel is written in order, so
    /// once the walk is past that boot there is nothing left that can attach to
    /// anything we kept.
    private static List<ParsedOffender> ReadOffendersBackTo(DateTime oldestBootStart)
    {
        var offenders = new List<ParsedOffender>();
        EventLogReader reader;
        try
        {
            reader = OpenReader(BootDegradationEventId);
        }
        catch (Exception)
        {
            // The boots were readable and these were not, so the boots still
            // stand — they simply arrive with nobody blamed.
            return offenders;
        }

        using (reader)
        {
            while (true)
            {
                var xml = NextRecordXml(reader);
                if (xml is null) break;
                ParsedOffender? parsed;
                try
                {
                    parsed = BootEventParser.ReadOffender(xml);
                }
                catch (Exception)
                {
                    continue;
                }
                if (parsed is null) continue;
                if (parsed.BootStarted < oldestBootStart) break;
                offenders.Add(parsed);
            }
        }
        return offenders;
    }

    /// The XML of the next record, or null when the channel is exhausted or the
    /// reader itself has failed. A reader that throws is finished — retrying it
    /// would spin — so unlike a bad record this ends the walk.
    private static string? NextRecordXml(EventLogReader reader)
    {
        try
        {
            using var record = reader.ReadEvent();
            return record?.ToXml();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static EventLogReader OpenReader(int eventId) =>
        new(new EventLogQuery(ChannelName, PathType.LogName, $"*[System[(EventID={eventId})]]")
        {
            ReverseDirection = true,   // newest boot first — "recent" is the whole point
        });
}
