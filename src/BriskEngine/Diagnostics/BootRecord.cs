using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

/// One boot, as Windows measured it (event ID 100), carrying every program
/// Windows blamed for it (event ID 101). Windows timed this itself, which is
/// what makes it stronger than any heuristic brisk could invent.
///
/// Field names verified against a live channel on Windows 11 build 26200. The
/// documented PostBootTime and BootDegradationTime do not exist under those
/// names on this build — the payload calls them BootPostBootTime and
/// BootDegradationDelta — so nothing here depends on either spelling.
public sealed record BootRecord(
    // When the boot began, from BootStartTime (UTC). Deliberately not the
    // event's own timestamp: Windows writes the ID 100 record once post-boot
    // settles, which on this machine was measured 13 hours after the boot it
    // describes. "Your boot on Monday evening" has to mean the boot.
    DateTime When,
    int BootMs,

    // Time to the point the desktop is usable. Nullable on purpose: Windows
    // not recording it is not the same as it being zero, and a zero here would
    // let a consumer compute BootMs - MainPathMs and blame a user's own
    // programs for 100% of a boot they had nothing to do with.
    int? MainPathMs,

    // Everything Windows blamed for this boot, worst first. Always complete for
    // the boot — never truncated — so a consumer saying "Windows blames these
    // three" can be sure there was no fourth.
    IReadOnlyList<BootOffender> Offenders);

/// One program Windows blamed for slowing a boot (ID 101). DegradationMs is the
/// part Windows attributes to this program beyond what it expected of it; the
/// offenders of one boot do not sum to that boot's total.
///
/// It carries no timestamp of its own: it only ever reaches a caller attached
/// to the BootRecord it belongs to, and a second near-identical timestamp (the
/// ID 101 record is written microseconds after its ID 100) would be one more
/// thing that can silently disagree with its parent.
public sealed record BootOffender(
    string Name,          // "Spotify.exe"
    string FriendlyName,  // "Spotify" — genuinely empty for some programs
    string Path,
    int DegradationMs);
