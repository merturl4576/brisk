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
///
/// Every failure here loses information rather than inventing it: a record that
/// will not read is skipped, never guessed at. That is the right trade for this
/// data, but it does mean a result can be short of what Windows logged, which
/// is why nothing in this file or on BootRecord claims completeness.
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
        // reading, and the earliest of those bounds the walk. Reading every
        // ID 101 in the channel would mean scanning years of history to attach
        // a handful of names.
        var offenders = ReadOffendersBackTo(BootEventParser.OldestStart(boots));
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
                var step = NextRecord(reader);
                if (step.Ended) break;
                if (step.Xml is null) continue;   // a record that would not render
                ParsedBoot? parsed;
                try
                {
                    parsed = BootEventParser.ReadBoot(step.Xml);
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
    /// boot that started before `oldestBootStart`. The channel is written in
    /// order, so once the walk is past the earliest boot we kept there is
    /// nothing left that can attach to anything.
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
                var step = NextRecord(reader);
                if (step.Ended) break;
                if (step.Xml is null) continue;
                ParsedOffender? parsed;
                try
                {
                    parsed = BootEventParser.ReadOffender(step.Xml);
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

    /// One step of a walk: XML to parse, a record to skip, or the end of the
    /// channel. The two failures are genuinely different and must not share an
    /// outcome. A reader that throws is finished — retrying it would spin — so
    /// that ends the walk. A record that will not render is one record: the
    /// reader has already advanced past it, so skipping cannot spin, and
    /// stopping there would silently hide every older record behind it.
    private readonly record struct RecordStep(string? Xml, bool Ended)
    {
        internal static RecordStep End() => new(null, true);
        internal static RecordStep Skip() => new(null, false);
        internal static RecordStep Read(string xml) => new(xml, false);
    }

    private static RecordStep NextRecord(EventLogReader reader)
    {
        EventRecord? record;
        try
        {
            record = reader.ReadEvent();
        }
        catch (Exception)
        {
            return RecordStep.End();
        }
        if (record is null) return RecordStep.End();

        using (record)
        {
            try
            {
                return RecordStep.Read(record.ToXml());
            }
            catch (Exception)
            {
                return RecordStep.Skip();
            }
        }
    }

    private static EventLogReader OpenReader(int eventId) =>
        new(new EventLogQuery(ChannelName, PathType.LogName, $"*[System[(EventID={eventId})]]")
        {
            ReverseDirection = true,   // newest boot first — "recent" is the whole point
        });
}
