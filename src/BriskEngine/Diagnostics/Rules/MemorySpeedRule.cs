using System.Globalization;
using System.Linq;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Memory set well below the speed its own modules are rated for — the "I have
/// had this PC for two years and never enabled XMP" case, which is invisible
/// from inside Windows and costs real performance.
///
/// The threshold is the whole rule, and real hardware set it. An earlier
/// 200 MT/s gap would have fired on the machine this was built against: two
/// modules rated 3200 running at 2933, a 267 MT/s gap that is not a disabled
/// profile at all — 2933 is a JEDEC speed and that platform's own ceiling.
/// brisk would have sent its maintainer into a BIOS to enable something that
/// would not have helped.
///
/// WMI exposes neither the memory controller's maximum nor whether an
/// XMP/EXPO profile exists, so the gap on its own does not identify its cause.
/// What can be recognised is the signature of a profile that was never
/// switched on: DDR4 falls back to its 2133 or 2400 JEDEC base, a third below
/// a 3200 rating, where a platform ceiling lands within a few hundred. Hence
/// SlowRatio — a proportion, not a difference.
///
/// And above the line the cause is still unknown, so the finding never states
/// one. It reports what was measured and names both explanations. Telling
/// someone to change a BIOS setting brisk cannot see, cannot verify and cannot
/// undo would be this category's characteristic lie.
public sealed class MemorySpeedRule : AdviseRuleBase
{
    /// Fire at or below 80% of rated. XMP-off on a 3200 kit lands at 2133
    /// (67%) or 2400 (75%); a platform ceiling like 2933 sits at 92% and stays
    /// quiet. The comparison is inclusive — exactly 80% is a finding.
    ///
    /// Known and accepted: this misses part of the case it was written for.
    /// A DDR5-5600 kit falling back to the JEDEC 4800 base with no profile
    /// ever enabled sits at 85.7%, above the line, and brisk says nothing.
    /// Same shape on DDR4 where a board's own default is 2666 under a 3200
    /// kit (83.3%). The miss is deliberate rather than an oversight, and it
    /// is not merely conservative: at those ratios the reading is genuinely
    /// indistinguishable from a platform ceiling, which is exactly what
    /// 2933/3200 (91.7%) is on the machine that set this number. A ratio high
    /// enough to catch 4800/5600 would sit around 0.86, which is inside the
    /// band where real ceilings live — a 3200 kit on a board capped at 2800 is
    /// 87.5% — so it would buy those findings by reintroducing the false
    /// alarm this threshold was rewritten to remove. Missing a true case is
    /// recoverable; telling someone to enable a profile that does not exist
    /// is the failure this rule is shaped around.
    internal const double SlowRatio = 0.80;

    public override string Id => "memory-speed";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var slow = ctx.Hardware.MemoryModules().Where(IsBelowRating).ToList();
        if (slow.Count == 0) return null;

        var readings = string.Join(", ", slow.Select(Reading));
        return new DiagnosticFinding(
            Id, $"rule.{Id}.title",
            "Memory is running below its rated speed",
            $"{readings} — each module's configured speed out of the speed it " +
            "is rated for. Two things look like this and Windows reports " +
            "neither: a memory speed profile (XMP/EXPO) that was never switched " +
            "on, or a board or memory controller that does not support the " +
            "rated speed. brisk cannot tell those apart from here, so it is not " +
            "telling you to change anything in the BIOS — only that the gap is there.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { readings });
    }

    /// A module is only slow if both of its numbers are real. A rated speed
    /// with no configured reading beside it is a module brisk cannot see the
    /// speed of, and treating that missing reading as zero would report the
    /// worst possible answer with no evidence for it at all — soldered laptop
    /// memory reports exactly that way.
    private static bool IsBelowRating(MemoryModule module) =>
        module.RatedMts > 0
        && module.ConfiguredMts > 0
        && module.ConfiguredMts <= module.RatedMts * SlowRatio;

    /// Slot, then configured out of rated, in the unit and the order the
    /// display rule already uses: what it is set to, out of what it could be.
    /// Nothing but numbers and symbols goes in here — the sentence around it
    /// is the localized part, and an English word smuggled into a data
    /// fragment would survive translation untranslated.
    ///
    /// MT/s, never MHz. DDR moves data twice per clock, so a module reported
    /// as 3200 runs a 1600 MHz clock; labelling this figure MHz would state
    /// double the real clock and is the single error a reader of this kind of
    /// output is most likely to catch.
    private static string Reading(MemoryModule module)
    {
        var speeds = string.Format(CultureInfo.InvariantCulture,
            "{0} MT/s / {1} MT/s", module.ConfiguredMts, module.RatedMts);
        // Firmware that filled in no slot label leaves the reading unlabelled
        // rather than carrying an invented name or a stray leading space.
        return string.IsNullOrWhiteSpace(module.Slot)
            ? speeds
            : $"{module.Slot.Trim()} {speeds}";
    }
}
