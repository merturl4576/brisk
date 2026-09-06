using System;
using System.Globalization;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

/// Windows Defender's blind spots: what it is told to skip, whether its two
/// protections are on, what mode it runs in, and how old its newest scan is.
/// Report-only; the one thing brisk offers is a link to Windows Security.
///
/// THE EVIDENCE CARRIES NUMBERS AND WINDOWS' OWN MODE WORD, nothing brisk
/// would have to translate: {0} excluded entries, {1} of the two protections
/// reading off, {2} of them unread, {3} the running mode as Windows names
/// it, {4} the age in days of the newest completed scan. Two states have no
/// number to put in a slot and get their own key: no scan on record, and an
/// exclusion list brisk could not read (an unelevated process — Windows
/// hides the list rather than refusing it, so null is the honest count).
///
/// The excluded paths never leave DefenderStatus.ExclusionPaths except onto
/// ScanSnapshot.DefenderExclusions, the Privacy page's own fold. Red line 2.
///
/// One WMI read per scan: Status memoizes on the context, and the posture
/// block reads the same answer.
public sealed class DefenderRule : PrivacyDisclosureRule
{
    public const string RuleId = "defender-blind-spots";
    public static readonly TimeSpan ScanAgeLimit = TimeSpan.FromDays(14);
    private const string MemoKey = "defender";
    private static readonly object Missing = new();

    private readonly Func<DateTime> _utcNow;

    public DefenderRule() : this(() => DateTime.UtcNow) { }

    public DefenderRule(Func<DateTime> utcNow) => _utcNow = utcNow;

    public override string Id => RuleId;

    /// The probe's answer for this scan, read once. A null answer is
    /// memoized too (as a sentinel), so a probe that failed is not asked
    /// again by the next reader.
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
        // `stamp` and not `n`: the count's own `n` is declared further down in
        // the same scope, and a pattern variable shares it.
        int? scanAgeDays = newest is { } stamp
            ? Math.Max(0, (int)(now - stamp).TotalDays)
            : null;

        var off = (s.RealTimeOn == false ? 1 : 0) + (s.TamperOn == false ? 1 : 0);
        var unread = (s.RealTimeOn is null ? 1 : 0) + (s.TamperOn is null ? 1 : 0);
        var mode = s.RunningMode ?? "unknown";
        var notNormal = !string.Equals(mode, "Normal", StringComparison.OrdinalIgnoreCase);
        var exclusions = s.ExclusionCount ?? 0;
        var exclusionsUnread = s.ExclusionCount is null;
        var stale = scanAgeDays is null || now - newest!.Value > ScanAgeLimit;

        if (exclusions == 0 && !exclusionsUnread && off == 0 && unread == 0 && !notNormal && !stale)
            return null;

        var n = exclusions.ToString(CultureInfo.InvariantCulture);
        var offText = off.ToString(CultureInfo.InvariantCulture);
        var unreadText = unread.ToString(CultureInfo.InvariantCulture);
        var protections =
            $"{offText} of the two protections brisk checks, real-time and tamper, " +
            $"read as off, and {unreadText} could not be read. Defender's running mode " +
            $"reads {mode}.";
        var headline = Lead(s, exclusions, mode, notNormal, scanAgeDays, stale);

        // THE EXCLUSION LIST BRISK COULD NOT READ. What the finding is called
        // depends on whether anything else here was MEASURED as bad. With
        // everything brisk did read reading fine, the only reason this fires
        // is the reading that never arrived, and titling that "Defender has
        // blind spots" claims blind spots off a hole: the unread title, no
        // headline, and the page bands it with what brisk could not read.
        // With something else measured bad, the title is the ordinary one and
        // that clause leads. The evidence is the same sentence either way —
        // the measured clauses plus "could not read the list".
        if (exclusionsUnread)
            return Disclosure(
                headline is null ? $"rule.{Id}.title.unread" : $"rule.{Id}.title",
                headline is null ? UnreadTitleText : TitleText,
                $"rule.{Id}.evidence.exclusions-unread",
                "brisk could not read Defender's exclusion list without administrator " +
                "rights, so it does not say how many entries are skipped. " + protections +
                " " + TrailerWithoutPaths,
                new[] { offText, unreadText, mode },
                headline);

        if (scanAgeDays is null)
            return Disclosure($"rule.{Id}.title", TitleText,
                $"rule.{Id}.evidence.no-scan",
                $"{n} entries are excluded from scanning. " + protections +
                " No completed scan is on record. " + Trailer,
                new[] { n, offText, unreadText, mode }, headline);

        var age = scanAgeDays.Value.ToString(CultureInfo.InvariantCulture);
        return Disclosure($"rule.{Id}.title", TitleText,
            $"rule.{Id}.evidence",
            $"{n} entries are excluded from scanning. " + protections +
            $" The newest completed scan is {age} days old. " + Trailer,
            new[] { n, offText, unreadText, mode, age }, headline);
    }

    /// THE WORST CLAUSE BRISK MEASURED, and never one it did not. Every state
    /// this rule reports as bad leads with the reading that makes it bad, in
    /// the order below, first match winning: what Defender is told to skip,
    /// then each protection that reads off, then a mode that is not Windows'
    /// normal one, then a scan too old, then no scan at all.
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
        bool notNormal, int? scanAgeDays, bool stale)
    {
        if (exclusions > 0)
            return Reading(exclusions.ToString(CultureInfo.InvariantCulture),
                "paths Defender skips", "");
        if (s.RealTimeOn == false) return Word("off", "Off", "real-time protection", ".realtime");
        if (s.TamperOn == false) return Word("off", "Off", "tamper protection", ".tamper");
        if (notNormal) return Reading(mode, "Defender mode", ".mode");
        if (stale && scanAgeDays is { } days)
            return Reading(days.ToString(CultureInfo.InvariantCulture),
                "days since the last scan", ".scan-age");
        if (scanAgeDays is null)
            return Word("none", "None", "completed scans on record", ".no-scan");
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

    /// Said wherever brisk DID read something. The exclusions-unread state
    /// keeps the second half and drops the first: there are no paths on the
    /// page to point at when the list is the thing brisk could not read.
    private const string Trailer =
        "The excluded paths are on this page and on nothing built to be shared. " +
        "brisk reads what Defender reports about itself and nothing past it; " +
        "whether another product protects this machine is not something that " +
        "read can tell you.";

    /// The same limit, without the sentence about paths brisk has none of.
    private const string TrailerWithoutPaths =
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

    /// The title for the two states where brisk has no bad reading of its
    /// own to report: nothing about Defender could be read at all, and an
    /// exclusion list that could not be read with everything else reading
    /// fine. Both carry no headline, which is what puts them in the page's
    /// could-not-read band.
    private const string UnreadTitleText =
        "Windows Defender's state could not be established";
}
