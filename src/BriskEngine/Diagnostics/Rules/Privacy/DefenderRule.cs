using System;
using System.Collections.Generic;
using System.Globalization;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Windows Defender's blind spots: what it is told to skip, whether its two
/// protections are on, what mode it runs in, and how old its newest scan is.
/// Report-only; the one thing brisk offers is a link to Windows Security.
///
/// THE EVIDENCE IS FIVE CLAUSES AND ALWAYS ALL FIVE: what Defender is told
/// to skip, how many of the two protections read off and how many could not
/// be read, the running mode as Windows names it, the age of the newest
/// completed scan, and where the entries are. Each clause has a measured form
/// carrying a number or Windows' own word, and a form for the reading that
/// did not arrive — an exclusion list an unelevated process cannot see, a
/// mode Windows named nothing for, a scan date that never arrived. The
/// evidence key names which forms were taken, which is why there are ten of
/// them.
///
/// The excluded entries never leave DefenderStatus.Exclusions except onto
/// ScanSnapshot.DefenderExclusions, the Privacy page's own fold. Red line 2.
///
/// One WMI read per scan: Status memoizes on the context, and the posture
/// block reads the same answer.
public sealed class DefenderRule : PrivacyDisclosureRule
{
    public const string RuleId = "defender-blind-spots";
    /// Whole days, because whole days is what the finding reports. See
    /// Detect: one measure, so the boundary and the printed age cannot
    /// disagree.
    public const int ScanAgeLimitDays = 14;
    private const string MemoKey = "defender";
    private static readonly object Missing = new();

    private readonly Func<DateTime> _utcNow;

    public DefenderRule() : this(() => DateTime.UtcNow) { }

    public DefenderRule(Func<DateTime> utcNow) => _utcNow = utcNow;

    public override string Id => RuleId;

    /// The probe's answer for this scan, read once. A null answer is
    /// memoized too (as a sentinel), so a probe that failed is not asked
    /// again by the next reader.
    ///
    /// GetOrAdd's value factory is not atomic and DiagnosticContext reserves
    /// the right to run rules in parallel, so two readers can race into the
    /// probe. The worst case is a second WMI round trip whose answer is
    /// dropped — never a wrong answer, because both readers ask the same
    /// machine the same question.
    public static DefenderStatus? Status(DiagnosticContext ctx)
    {
        var memo = ctx.Memo.GetOrAdd(MemoKey, _ =>
        {
            try { return (object?)ctx.Defender.Read() ?? Missing; }
            catch (Exception) { return Missing; }
        });
        return memo as DefenderStatus;
    }

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var s = Status(ctx);
        if (s is null) return Unread();

        var now = _utcNow();
        DateTime? newest = (s.LastQuickScanUtc, s.LastFullScanUtc) switch
        {
            ({ } q, { } f) => q > f ? q : f,
            ({ } q, null) => q,
            (null, { } f) => f,
            _ => null,
        };
        // WHOLE DAYS, and the age the reader is shown is the age the rule
        // judges. A TimeSpan comparison beside a truncated number made 14.5
        // days fire and then print "14 days old" — two measures of one
        // reading, disagreeing in the one place a reader can see both.
        int? scanAgeDays = newest is { } stamp
            ? Math.Max(0, (int)(now - stamp).TotalDays)
            : null;

        var off = (s.RealTimeOn == false ? 1 : 0) + (s.TamperOn == false ? 1 : 0);
        var unread = (s.RealTimeOn is null ? 1 : 0) + (s.TamperOn is null ? 1 : 0);
        // A MODE BRISK DID NOT READ IS NOT A MODE. Coalescing null to the word
        // "unknown" made an unread reading into a measured one — and shipped
        // brisk's own English through the value slot, where no resx can reach
        // it. Null is its own clause, with a key.
        // Empty counts as unread beside null: a mode with no name in it is
        // not a mode Windows told brisk about, and it would otherwise lead
        // with an empty headline.
        var modeUnread = string.IsNullOrEmpty(s.RunningMode);
        var mode = s.RunningMode ?? "";
        var notNormal = !modeUnread
            && !string.Equals(mode, "Normal", StringComparison.OrdinalIgnoreCase);
        var exclusions = s.ExclusionCount ?? 0;
        var exclusionsUnread = s.ExclusionCount is null;
        // NO SCAN ON RECORD IS A READING, AND ONLY WHEN THERE WAS ONE.
        // MSFT_MpComputerStatus answers with a row whatever it knows, and a
        // row that answered null for the protections and the mode answered
        // null for the two scan stamps as well — not because this machine
        // has never completed a scan, but because nothing in that row came
        // back. Absent stamps beside readings that did arrive ARE the
        // measured state: Windows leaves them empty until the first scan
        // finishes. The exclusion list does not count here — Windows hides
        // it from an unelevated process by design, which is the common case
        // and says nothing about the row (spec, section 3).
        var rowRead = s.RealTimeOn is not null && s.TamperOn is not null && !modeUnread;
        var noScan = scanAgeDays is null && rowRead;
        var scanUnread = scanAgeDays is null && !rowRead;
        var staleScan = scanAgeDays is { } days && days > ScanAgeLimitDays;

        if (exclusions == 0 && !exclusionsUnread && off == 0 && unread == 0
            && !modeUnread && !notNormal && !noScan && !staleScan)
            return null;

        // FIVE CLAUSES, ALWAYS ALL FIVE, each in the form the reading took:
        // what is skipped, the two protections, the mode, the newest scan,
        // and where the entries are. The sentence is the whole reading and not
        // its bad half (spec), which is also what keeps a headline from
        // leading with a clause the sentence never made — the state that
        // could not read the exclusion list still says how old the scan is.
        //
        // The key names which forms were taken, so a reader of the resx can
        // see the same ten sentences the rule can assemble, and the args
        // arrive in the order the clauses do. ".mode-unread.no-scan" is not
        // among them and cannot be: an unread mode is a row that did not
        // answer, and a row that did not answer never measured "no scan".
        var key = $"rule.{Id}.evidence"
            + (exclusionsUnread ? ".exclusions-unread" : "")
            + (modeUnread ? ".mode-unread" : "")
            + (noScan ? ".no-scan" : "")
            + (scanUnread ? ".scan-unread" : "");
        var args = new List<string>();

        var skipped = ExclusionsUnreadClause;
        if (!exclusionsUnread)
        {
            var n = exclusions.ToString(CultureInfo.InvariantCulture);
            args.Add(n);
            skipped = $"{n} entries are excluded from scanning.";
        }

        var offText = off.ToString(CultureInfo.InvariantCulture);
        var unreadText = unread.ToString(CultureInfo.InvariantCulture);
        args.Add(offText);
        args.Add(unreadText);
        var protections =
            $"{offText} of the two protections brisk checks, real-time and tamper, " +
            $"read as off, and {unreadText} could not be read.";

        var running = ModeUnreadClause;
        if (!modeUnread)
        {
            args.Add(mode);
            running = $"Defender's running mode reads {mode}.";
        }

        var scan = noScan ? "No completed scan is on record." : ScanUnreadClause;
        if (scanAgeDays is { } scanned)
        {
            var age = scanned.ToString(CultureInfo.InvariantCulture);
            args.Add(age);
            scan = $"The newest completed scan is {age} days old.";
        }

        var headline = Lead(s, exclusions, mode, notNormal, scanAgeDays, staleScan, noScan);

        // WHAT THE FINDING IS CALLED FOLLOWS WHAT IT LED WITH. A headline is
        // this family's word for "brisk read this", so a finding with none
        // fires only because something could not be read — an exclusion list,
        // a protection, the mode — and the unread title is what says so.
        // Anything measured bad takes the rule's own name and leads with that
        // reading, however many other parts went unread beside it.
        return Disclosure(
            headline is null ? $"rule.{Id}.title.unread" : $"rule.{Id}.title",
            headline is null ? UnreadTitleText : TitleText,
            key,
            string.Join(" ", skipped, protections, running, scan,
                exclusionsUnread ? TrailerWithoutEntries : Trailer),
            args, headline);
    }

    /// THE WORST CLAUSE BRISK MEASURED, and never one it did not. Every state
    /// this rule reports as bad leads with the reading that makes it bad, in
    /// the order below, first match winning: what Defender is told to skip,
    /// then each protection that reads off, then a mode that is not Windows'
    /// normal one, then a scan too old, then a machine brisk read as having
    /// completed none.
    ///
    /// WHY EVERY MEASURED STATE HAS ONE. A disclosure with no headline is how
    /// this family says "nothing was read": PrivacyViewModel bands exactly
    /// those into the page's could-not-read list and the report card prints
    /// their titles under the same heading. A rule that read "real-time
    /// protection is off" and led with nothing would be filed there as
    /// something brisk failed to read — on a card built to be shared. Null
    /// comes back only for a machine with nothing measured to report, which
    /// is either silence or the unread finding.
    ///
    /// A WORD IS A WHOLE KEY, A READING IS AN ARGUMENT. "Off" and "None" are
    /// brisk's own English and would reach a Turkish reader untranslated as
    /// arguments — RecallStatusRule made the same call for the same reason.
    /// The running mode is Windows' word for its own state, so it travels as
    /// an argument and is not translated by anybody.
    private Headline? Lead(DefenderStatus s, int exclusions, string mode,
        bool notNormal, int? scanAgeDays, bool staleScan, bool noScan)
    {
        if (exclusions > 0)
            return Reading(exclusions.ToString(CultureInfo.InvariantCulture),
                "entries Defender skips", "");
        if (s.RealTimeOn == false) return Word("off", "Off", "real-time protection", ".realtime");
        if (s.TamperOn == false) return Word("off", "Off", "tamper protection", ".tamper");
        if (notNormal) return Reading(mode, "Defender mode", ".mode");
        if (staleScan && scanAgeDays is { } days)
            return Reading(days.ToString(CultureInfo.InvariantCulture),
                "days since the last scan", ".scan-age");
        // noScan, not "scanAgeDays is null": a stamp that is missing because
        // the row answered nothing is a reading brisk failed to take, and
        // this family leads with nothing when that is all it has.
        if (noScan)
            return Word("none", "None", "completed scans on record", ".no-scan");
        // Nothing brisk MEASURED is bad here, so nothing leads: what is left
        // is one or more readings that did not arrive, and the caller says so
        // with the unread title.
        return null;
    }

    /// A value brisk read, substituted into the one numeric value key.
    private Headline Reading(string value, string caption, string captionSuffix) =>
        new(value, caption,
            $"rule.{Id}.headline.value", new[] { value },
            $"rule.{Id}.headline.caption{captionSuffix}", Array.Empty<string>());

    /// A value that is a word of brisk's own, which is a key and not an
    /// argument.
    private Headline Word(string valueSuffix, string english, string caption,
        string captionSuffix) =>
        new(english, caption,
            $"rule.{Id}.headline.value.{valueSuffix}", Array.Empty<string>(),
            $"rule.{Id}.headline.caption{captionSuffix}", Array.Empty<string>());

    private const string TitleText = "Windows Defender has blind spots";

    /// The exclusion clause when the list could not be read. Never a zero:
    /// Windows hides the list from a process that is not elevated, and a
    /// count brisk did not take is not a count of nothing.
    private const string ExclusionsUnreadClause =
        "brisk could not read Defender's exclusion list without administrator " +
        "rights, so it does not say how many entries are skipped.";

    /// The mode clause when Windows named no mode. Its own sentence rather
    /// than a word in the measured one, so no reader is told brisk read
    /// something it did not.
    private const string ModeUnreadClause =
        "brisk could not read Defender's running mode.";

    /// The scan clause when the row that carries the two scan stamps
    /// answered nothing else either. "No completed scan is on record" is a
    /// claim about this machine; two empty stamps out of a row that read
    /// null throughout are a claim about the read.
    private const string ScanUnreadClause =
        "brisk could not read when Defender last completed a scan.";

    /// Said wherever brisk DID read something. The exclusions-unread state
    /// keeps the second half and drops the first: there are no entries on the
    /// page to point at when the list is the thing brisk could not read.
    ///
    /// ENTRIES, because the fold holds every kind the count counted — a path,
    /// an extension, a process — and a sentence that named only the paths
    /// would describe a shorter list than the one below it.
    private const string Trailer =
        "The excluded entries are on this page and on nothing built to be shared. " +
        "brisk reads what Defender reports about itself and nothing past it; " +
        "whether another product protects this machine is not something that " +
        "read can tell you.";

    /// The same limit, without the sentence about entries brisk has none of.
    private const string TrailerWithoutEntries =
        "brisk reads what Defender reports about itself and nothing past it; " +
        "whether another product protects this machine is not something that " +
        "read can tell you.";

    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread",
        UnreadTitleText,
        $"rule.{Id}.evidence.unread",
        "brisk asked Windows Defender about itself and got nothing back it can " +
        "read. Defender may be absent, replaced by another product, or refusing " +
        "the question; brisk cannot tell which from here and does not report " +
        "any of them as fine.");

    /// The title wherever brisk has no bad reading of its own to report.
    /// That is the whole-read failure — nothing about Defender came back at
    /// all — and every finding where what is missing is one of the four
    /// readings Lead would have led with: the exclusion list, real-time
    /// protection, tamper protection, the running mode. Any one of them can
    /// go unread while the rest read fine. A scan stamp that never arrived
    /// is here too, and never on its own: it is unread precisely when one of
    /// those four was. None of these carries a headline,
    /// which is what puts them in the page's could-not-read band.
    private const string UnreadTitleText =
        "Windows Defender's state could not be established";
}
