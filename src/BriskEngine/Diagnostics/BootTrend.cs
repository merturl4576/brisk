using System;
using System.Collections.Generic;
using System.Linq;

namespace BriskEngine.Diagnostics;

/// The performance read-back: this machine's boots before brisk's first
/// change, next to its boots since the last one — both from Windows' own
/// timings (event ID 100), never from a stopwatch brisk invented.
///
/// It deliberately claims no causality. The copy built from this says
/// "before / since", never "brisk made your boot 18 s faster": a Windows
/// update, a new program or a warmer disk cache sit in the same window,
/// and the one thing that keeps this surface honest is that it only ever
/// prints two measurements and their dates side by side.
public sealed record BootTrend(
    int BeforeMedianMs, int BeforeBoots,
    int AfterMedianMs, int AfterBoots,
    DateTime FirstChangeUtc, DateTime LastChangeUtc);

public static class BootTrendCalculator
{
    /// Below this many boots on a side there is no "typical boot" for that
    /// side, only an anecdote — the same bar BootDegradationRule sets.
    /// The AFTER side is allowed to be smaller (1+): the first boot after a
    /// change is exactly the reader's question, and the printed count says
    /// how thin the evidence still is.
    public const int MinimumBefore = 3;

    /// More history than the degradation rule samples: the BEFORE window
    /// has to reach behind the first change, which may be days old.
    public const int SampledBoots = 20;

    /// Boots BETWEEN the first and last change are dropped, not classified:
    /// a boot taken halfway through a series of changes measures neither
    /// the before-machine nor the after-machine.
    public static BootTrend? Compute(IReadOnlyList<BootRecord> boots,
        DateTime? firstChangeUtc, DateTime? lastChangeUtc)
    {
        if (firstChangeUtc is null || lastChangeUtc is null) return null;
        var before = boots.Where(b => b.When < firstChangeUtc.Value)
            .Select(b => b.BootMs).ToList();
        var after = boots.Where(b => b.When > lastChangeUtc.Value)
            .Select(b => b.BootMs).ToList();
        if (before.Count < MinimumBefore || after.Count == 0) return null;
        return new BootTrend(Median(before), before.Count,
            Median(after), after.Count,
            firstChangeUtc.Value, lastChangeUtc.Value);
    }

    /// The lower middle on an even sample — a boot this machine actually
    /// had, where the average of two would be a number nothing measured
    /// (BootDegradationRule's convention, kept so the two surfaces can
    /// never print different "typical" boots from the same records).
    private static int Median(List<int> values)
    {
        var sorted = values.OrderBy(ms => ms).ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }
}
