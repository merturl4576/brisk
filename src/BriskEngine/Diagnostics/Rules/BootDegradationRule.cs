using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Two true things, side by side: how long this machine takes to boot, and
/// which programs Windows itself recorded as starting slower than it expected.
///
/// They are never joined. The sentence this rule was originally planned to
/// produce — "boot takes 57 s and 37 s of it belongs to these three" — is
/// false, and the data proves it: on the machine this was built against a
/// 51.2 s boot had nobody blamed at all while a *faster* 45.3 s boot had two,
/// and three of the ten most recent boots named nobody. DegradationTime means
/// "this program started slower than Windows expected", not "this program
/// added that much to your boot", so no sum, share or subtraction of the two
/// numbers appears anywhere in the output.
///
/// Advise on purpose. The Startup page owns the switches; wiring a fix button
/// through here would mean mapping an executable name to a startup entry, and
/// that match is fuzzy enough that a wrong one would disable the wrong program.
public sealed class BootDegradationRule : AdviseRuleBase
{
    /// One bad boot after an update is normal. Below this many records there is
    /// no typical boot to report, only an anecdote.
    internal const int MinimumBoots = 3;

    /// The maintainer's machine sits at 57 s and is genuinely slow; a 20 s boot
    /// is not a finding. Strictly exceeded, so 40 s exactly stays quiet.
    internal const int SlowBootMs = 40_000;

    internal const int TopOffenders = 3;

    /// Enough boots for one outlier not to decide the median, few enough that
    /// "recent" still means recent. A ceiling on what is asked for, not a
    /// promise of what arrives: RealEventLogProbe skips an ID 100 record it
    /// cannot read and keeps walking, so this is the most recent boots brisk
    /// could *read* rather than the most recent boots that happened — which is
    /// why the copy says so in those words.
    internal const int SampledBoots = 8;

    /// A bound on how many blamed records are worth aggregating. It is applied
    /// whole boots at a time, never mid-boot: cutting inside one boot is the
    /// exact failure IEventLogProbe was shaped to avoid, and a partial boot
    /// could hand back a smaller "worst" reading for a program than Windows
    /// actually recorded. On real data 8 boots carried 14 offenders, so this
    /// is a ceiling rather than something that normally bites.
    internal const int SampledOffenders = 25;

    public override string Id => "boot-degradation";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var boots = ctx.EventLog.RecentBoots(SampledBoots);
        if (boots.Count < MinimumBoots) return null;

        var medianMs = MedianBootMs(boots);
        if (medianMs <= SlowBootMs) return null;

        var median = Seconds(medianMs);
        var sampled = boots.Count.ToString(CultureInfo.InvariantCulture);
        var (blamed, bootsCounted) = WorstPerProgram(boots);

        // MainPathMs is deliberately untouched. BootMs - MainPathMs equals
        // BootPostBootTime, a field Windows already publishes by name, and it
        // splits the boot into phases rather than into "Windows versus your
        // programs" — four of the five programs named on the verified machine
        // are Microsoft's own.

        if (blamed.Count == 0)
            return Finding(median, sampled, names: null,
                $"Boot takes about {median}, the middle of the {sampled} most recent " +
                "boots brisk could read from Windows' own timings. No program stood out " +
                "on those boots — everything Windows watched started about as fast as it " +
                "expected. That is a normal result rather than a missing answer: the boot " +
                "is slow without any one program to point at.");

        var names = string.Join(", ", blamed.Select(b =>
            $"{Label(b.Offender)} {Seconds(b.Offender.DegradationMs)} ({b.Boots}/{bootsCounted})"));
        return Finding(median, sampled, names,
            $"Boot takes about {median}, the middle of the {sampled} most recent boots " +
            $"brisk could read from Windows' own timings. Windows blamed these for " +
            $"starting slower than it expected: {names} — that is how late each one was, " +
            "not time it added to your boot. The bracketed figure is how many of those " +
            "boots blamed it, so something that happens every start reads differently " +
            "from a one-off, and a program blamed once may not start with Windows any " +
            "more. Windows' own components often top the list and brisk will not switch " +
            "those off; look for the rest under Startup programs on the Performance page.");
    }

    private DiagnosticFinding Finding(string median, string sampled, string? names, string evidence) =>
        new(Id, $"rule.{Id}.title",
            "Windows takes a long time to start", evidence,
            Severity.Warning, Category, ImpactStars: 4, CanFix: false, FixDescription: null,
            EvidenceKey: names is null ? $"rule.{Id}.evidence.nobody" : $"rule.{Id}.evidence",
            EvidenceArgs: names is null
                ? new[] { median, sampled }
                : new[] { median, sampled, names });

    /// The middle reading, and on an even sample the lower of the two middles
    /// — a boot this machine actually had, where the average of two would be a
    /// number nothing ever measured.
    private static int MedianBootMs(IReadOnlyList<BootRecord> boots)
    {
        var sorted = boots.Select(b => b.BootMs).OrderBy(ms => ms).ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }

    /// One row per program, carrying its worst reading across the sample — not
    /// a sum. Windows blaming Defender for 7.7 s on Monday and 52.7 s on
    /// Tuesday is one program that was once 52.7 s late, never a 60 s program.
    ///
    /// Keyed on Name, so two unrelated programs sharing an executable name —
    /// Google's updater.exe and Tor's, both seen on the verified machine —
    /// collapse into one row. That loses a name rather than misattributing one:
    /// the label and the number always come from the same record, so the row
    /// shown is always true of the program it names. An omission is what
    /// "Windows blamed these three", never "only these three", already covers.
    private static (List<(BootOffender Offender, int Boots)> Blamed, int BootsCounted)
        WorstPerProgram(IReadOnlyList<BootRecord> boots)
    {
        var worst = new Dictionary<string, BootOffender>(StringComparer.OrdinalIgnoreCase);
        // Boots that blamed each program, never records. Windows writes one
        // record per program per START: mscorsvw.exe was blamed twice inside
        // a single boot on the verified machine, and counting records would
        // have turned that one-off into two boots out of three.
        var bootsBlaming = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seen = 0;
        // The denominator of the bracketed figure, and deliberately not
        // boots.Count: the record bound below can stop the walk early, and a
        // count taken over five boots must not be printed as "of eight". The
        // two are equal on every machine the bound does not bite — which is
        // every machine seen so far: 8 boots carried 14 records.
        var bootsCounted = 0;
        foreach (var boot in boots)
        {
            if (seen >= SampledOffenders) break;
            bootsCounted++;
            var countedHere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var offender in boot.Offenders)
            {
                seen++;
                // Nothing honest to say about a program with no name at all,
                // and a reading that rounds to "0 s" is not a sentence — under
                // half a second is not worth naming beside a boot measured in
                // tens of seconds.
                if (Label(offender).Length == 0) continue;
                if (offender.DegradationMs < 500) continue;
                if (!worst.TryGetValue(offender.Name, out var held)
                    || offender.DegradationMs > held.DegradationMs)
                    worst[offender.Name] = offender;
                if (countedHere.Add(offender.Name))
                    bootsBlaming[offender.Name] =
                        bootsBlaming.GetValueOrDefault(offender.Name) + 1;
            }
        }
        var blamed = worst.Values
            .OrderByDescending(o => o.DegradationMs)
            .ThenBy(Label, StringComparer.OrdinalIgnoreCase)   // ties stay stable
            .Take(TopOffenders)
            .Select(o => (Offender: o, Boots: bootsBlaming[o.Name]))
            .ToList();
        return (blamed, bootsCounted);
    }

    /// FriendlyName is genuinely empty for some programs — brisk-app.exe
    /// arrived with none — and a blank in the sentence is worse than the raw
    /// executable name.
    private static string Label(BootOffender offender) =>
        string.IsNullOrWhiteSpace(offender.FriendlyName)
            ? offender.Name.Trim()
            : offender.FriendlyName.Trim();

    private static string Seconds(int ms) =>
        ((int)Math.Round(ms / 1000.0, MidpointRounding.AwayFromZero))
            .ToString(CultureInfo.InvariantCulture) + " s";
}
