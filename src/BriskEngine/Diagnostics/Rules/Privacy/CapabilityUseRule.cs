using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules.Privacy;

public enum Capability { Microphone, Camera }

/// One app that used the microphone or the camera inside the window, as
/// Windows recorded it. THE ONLY SHAPE AN APP NAME OR PATH TRAVELS IN, and
/// it travels one way: EngineHost puts these on ScanSnapshot.CapabilityUses
/// and the Privacy page renders them behind a fold. Nothing here is on a
/// DiagnosticFinding, so no report card, headline or JSON can carry a name
/// however the rest of the pipeline is wired — spec red line 2, held by
/// construction. Path is set for desktop apps only; packaged apps have none.
public sealed record CapabilityUseRecord(
    Capability Capability, string AppName, string? Path, DateTime LastUsedUtc);

/// Which apps used the microphone and the camera in the last 30 days, from
/// CapabilityAccessManager's consent store: one subkey per packaged app (a
/// package family name) and, under NonPackaged, one per desktop executable
/// with '#' where its path has '\'. Each carries two FILETIME QWORDs:
/// LastUsedTimeStart, written when the app ACQUIRED the device, and
/// LastUsedTimeStop, written when it RELEASED it.
///
/// A USE IS DATED BY STOP, AND BY START WHEN THERE IS NO STOP. Windows
/// leaves Stop at 0 for a session that never ended — a device still held,
/// which Settings shows as "currently in use", and an app that crashed
/// without releasing one — so a rule reading Stop alone would drop exactly
/// the persistent listener it exists to surface. Both stamps 0 or absent is
/// the only thing that means never used. brisk does not report the
/// Start-only case as in use NOW: an open session and a crash remnant read
/// identically from here, and neither is something this read measured.
///
/// ONE APP COUNTS ONCE. Discord and Wispr Flow keep one subkey per installed
/// version, and the machine this was written on holds 40 Discord entries for
/// one program. Desktop apps group by file name, packaged apps by the part
/// of the family name before the underscore; the newest stamp in a group is
/// the record's date.
///
/// UNREADABLE IS NOT ZERO. An empty subkey list is what the probe answers
/// for a missing key and for an empty one alike, so the root's own Value
/// (Allow/Deny — present on every real machine) is the second read that
/// tells them apart: no subkeys AND no Value is a record brisk could not
/// read. A throw anywhere under one capability costs that capability and
/// nothing else.
///
/// The clock is injected so the window can be tested at its boundary;
/// nothing else in the rule reads one.
public sealed class CapabilityUseRule : PrivacyDisclosureRule
{
    public const string RuleId = "camera-mic-use";
    public const string StoreRoot =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";
    public const string MicrophoneKey = StoreRoot + @"\microphone";
    public const string WebcamKey = StoreRoot + @"\webcam";
    public const string NonPackaged = "NonPackaged";
    public const string LastUsedStopValueName = "LastUsedTimeStop";
    public const string LastUsedStartValueName = "LastUsedTimeStart";
    public static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private readonly Func<DateTime> _utcNow;

    public CapabilityUseRule() : this(() => DateTime.UtcNow) { }

    public CapabilityUseRule(Func<DateTime> utcNow) => _utcNow = utcNow;

    public override string Id => RuleId;

    public static string KeyFor(Capability capability) =>
        capability == Capability.Microphone ? MicrophoneKey : WebcamKey;

    /// The apps that used one capability inside the window, newest first,
    /// or null when that record could not be read at all.
    public static IReadOnlyList<CapabilityUseRecord>? ReadRecent(
        DiagnosticContext ctx, Capability capability, DateTime nowUtc)
    {
        try
        {
            var key = KeyFor(capability);
            var names = ctx.Registry.GetSubKeyNames(key);
            var rootValue = ctx.Registry.GetString(key, "Value");
            if (names.Count == 0 && rootValue is null) return null;

            var found = new Dictionary<string, CapabilityUseRecord>(StringComparer.OrdinalIgnoreCase);
            void Consider(string subKey, string appName, string? path)
            {
                // WHEN THE APP RELEASED THE DEVICE, OR FAILING THAT WHEN IT
                // TOOK IT. Stop is written when a session ENDS, so it is 0
                // while the device is still held — Settings' "currently in
                // use" — and after an app crashed without releasing one.
                // Reading Stop alone drops exactly the entry this rule exists
                // to surface, so Start is the FALLBACK — not a maximum. A
                // session that ended is dated by when it ended, which is what
                // "last used" means. KNOWN AND NOT CLAIMED AWAY: an app that
                // has taken the device AGAIN since — Start moved, Stop still
                // holding the earlier release — is therefore dated by that
                // earlier release. That is an older date than the truth, and
                // it is a date brisk measured; "in use now" is not, so brisk
                // does not print it. An open session and a crash remnant read
                // the same from here, and nothing measured which it is.
                var used = Stamp(ctx, subKey, LastUsedStopValueName)
                    ?? Stamp(ctx, subKey, LastUsedStartValueName);
                if (used is not { } when) return;
                if (when < nowUtc - Window) return;
                if (!found.TryGetValue(appName, out var prior) || prior.LastUsedUtc < when)
                    found[appName] = new CapabilityUseRecord(capability, appName, path, when);
            }

            foreach (var name in names)
            {
                if (string.Equals(name, NonPackaged, StringComparison.OrdinalIgnoreCase))
                {
                    var branch = $@"{key}\{NonPackaged}";
                    foreach (var exe in ctx.Registry.GetSubKeyNames(branch))
                    {
                        var path = exe.Replace('#', '\\');
                        Consider($@"{branch}\{exe}", System.IO.Path.GetFileName(path), path);
                    }
                }
                else
                {
                    var family = name.Split('_')[0];
                    Consider($@"{key}\{name}", family, null);
                }
            }
            return found.Values.OrderByDescending(r => r.LastUsedUtc).ToList();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// ONE FILETIME stamp, or nothing. Nothing is what a read that cannot be
    /// turned into a date returns: a value that is not there, a 0 — which
    /// Windows writes for a stamp it has never set, and which converts
    /// happily into the first instant of 1601 — and a number that will not
    /// convert at all. The conversion is caught here rather than escaping to
    /// the walk's own catch, where one bad entry would cost the capability.
    private static DateTime? Stamp(
        DiagnosticContext ctx, string subKey, string valueName)
    {
        var raw = ctx.Registry.GetLong(subKey, valueName);
        if (raw is null or <= 0) return null;
        try { return DateTime.FromFileTimeUtc(raw.Value); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    /// Both capabilities in one list, newest first. An unreadable half adds
    /// nothing; the finding is where "could not read" is said.
    public static IReadOnlyList<CapabilityUseRecord> ReadAllRecent(
        DiagnosticContext ctx, DateTime nowUtc) =>
        (ReadRecent(ctx, Capability.Microphone, nowUtc) ?? Array.Empty<CapabilityUseRecord>())
        .Concat(ReadRecent(ctx, Capability.Camera, nowUtc) ?? Array.Empty<CapabilityUseRecord>())
        .OrderByDescending(r => r.LastUsedUtc)
        .ToList();

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var now = _utcNow();
        var mic = ReadRecent(ctx, Capability.Microphone, now);
        var cam = ReadRecent(ctx, Capability.Camera, now);
        if (mic is null && cam is null) return Unread();

        var micCount = mic?.Count ?? 0;
        var camCount = cam?.Count ?? 0;
        if (micCount == 0 && camCount == 0 && mic is not null && cam is not null) return null;

        var micText = micCount.ToString(CultureInfo.InvariantCulture);
        var camText = camCount.ToString(CultureInfo.InvariantCulture);
        var headline = HeadlineFor(micCount, camCount);

        // ONE HALF UNREADABLE AND THE OTHER COUNTING NOTHING. Past the early
        // return above, a zero on both sides means one of them could not be
        // read — and the only reading that DID arrive is a zero, so nothing
        // here used anything. Titling that "Apps have used the microphone or
        // camera recently" claims a use brisk measured the absence of, over a
        // fold with nothing in it to back it up. The title is the unreadable
        // one and the headline is already null, which is also what routes the
        // row to the page's "what brisk could not read" band. The EVIDENCE
        // stays the half-unread sentence: an honest 0 for the half brisk read
        // and "could not be read" for the half it did not is exactly this
        // state, and the both-unread sentence would overstate the silence.
        var nothingCounted = micCount == 0 && camCount == 0;
        var titleKey = nothingCounted ? $"rule.{Id}.title.unread" : $"rule.{Id}.title";
        var title = nothingCounted ? UnreadTitleText : TitleText;

        if (mic is null)
            return Disclosure(titleKey, title,
                $"rule.{Id}.evidence.mic-unread",
                $"By Windows' own record of when each app last used it, {camText} apps " +
                "used the camera in the last 30 days. The microphone record could not " +
                "be read, so brisk says nothing about it. " + WhereTheNamesAre,
                new[] { camText }, headline);
        if (cam is null)
            return Disclosure(titleKey, title,
                $"rule.{Id}.evidence.cam-unread",
                $"By Windows' own record of when each app last used it, {micText} apps " +
                "used the microphone in the last 30 days. The camera record could not " +
                "be read, so brisk says nothing about it. " + WhereTheNamesAre,
                new[] { micText }, headline);
        return Disclosure($"rule.{Id}.title", TitleText,
            $"rule.{Id}.evidence",
            $"By Windows' own record of when each app last used them, {micText} apps " +
            $"used the microphone and {camText} used the camera in the last 30 days. " +
            WhereTheNamesAre + " " + WhatTheReadCannotTell,
            new[] { micText, camText }, headline);
    }

    private const string TitleText = "Apps have used the microphone or camera recently";

    /// The title for every state where brisk has no use to report: both
    /// records unreadable, and one unreadable with the other counting none.
    private const string UnreadTitleText =
        "Which apps used the microphone or camera could not be established";

    /// Said on every reading brisk did take, unreadable halves included: the
    /// finding carries counts, and the page it points at is the only place
    /// the names are.
    private const string WhereTheNamesAre =
        "Which ones, and when, is on this page and on nothing built to be shared.";

    /// The limit of the read, said where brisk took BOTH halves of it. The
    /// half-unread sentences carry their own limit — the record they could
    /// not read — and stacking a second one behind it buries the first.
    private const string WhatTheReadCannotTell =
        "brisk reads the record and nothing past it: what an app did with the " +
        "microphone or the camera is not something that read can tell you.";

    /// The larger count leads; a tie goes to the microphone. Null when there
    /// is nothing to lead with, which puts the row in the unreadable band.
    private Headline? HeadlineFor(int micCount, int camCount)
    {
        if (micCount == 0 && camCount == 0) return null;
        var mic = micCount >= camCount;
        var n = (mic ? micCount : camCount).ToString(CultureInfo.InvariantCulture);
        return new Headline(
            n,
            mic ? "apps used the microphone in the last 30 days"
                : "apps used the camera in the last 30 days",
            $"rule.{Id}.headline.value", new[] { n },
            $"rule.{Id}.headline.caption.{(mic ? "mic" : "cam")}", Array.Empty<string>());
    }

    /// No digits anywhere in this one: a reading that never arrived is not
    /// reported with a number, and the window is not mentioned by its length.
    private DiagnosticFinding Unread() => Disclosure(
        $"rule.{Id}.title.unread", UnreadTitleText,
        $"rule.{Id}.evidence.unread",
        "brisk looked where Windows records when each app last used the " +
        "microphone and the camera and found nothing there it could read. A " +
        "record with nothing in it and a record brisk could not open read the " +
        "same from here, so brisk does not report that no app used them.");
}
