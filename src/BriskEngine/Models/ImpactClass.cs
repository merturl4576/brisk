namespace BriskEngine.Models;

/// How much a Problem is allowed to move the health score.
///
/// Measured: brisk can read a number on the other side of the fix — boot
/// timings Windows wrote itself, the refresh rate the panel is running at,
/// days until the disk is full. Charged ImpactStars x severity weight.
///
/// Hygiene: a setting or a cache brisk believes is better the other way,
/// with no measurement that anyone will feel it — power plan, visual
/// effects, web results in the start menu, rebuildable caches. Charged a
/// flat 2 points (1 for Info), whatever its stars.
///
/// Hygiene is the default, so a rule that says nothing here under-charges
/// rather than over-charges: a score that jumps 40 points on switch flips
/// with nothing measured behind them is the category's oldest lie, and brisk
/// once told it (47 -> 90 on settings alone).
public enum ImpactClass { Hygiene, Measured }
