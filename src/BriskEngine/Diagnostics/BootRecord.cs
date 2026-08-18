using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

/// One boot, as Windows measured it (event ID 100), carrying the programs
/// Windows blamed for it (event ID 101). Windows timed this itself, which is
/// what makes it stronger than any heuristic brisk could invent.
///
/// The offender list is best effort, not a guaranteed-complete set — see the
/// comment on Offenders below for what can be missing and how to phrase a
/// result built on it.
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
    // not recording it is not the same as it being zero, and a zero would make
    // BootMs - MainPathMs come out as the whole boot.
    //
    // That difference is worth naming, because it is tempting to read it as
    // "Windows versus your own programs" and it is not. On both verified
    // payloads it equals BootPostBootTime exactly (51237 - 24437 = 26800;
    // 111814 - 25314 = 86500), so the subtraction only reproduces a field
    // Windows already publishes by name. It is a phase split — main path versus
    // post-boot — not a system-versus-you split. The user's own programs are
    // not absent from either half: brisk-app.exe was blamed for 26081 ms of one
    // measured boot. But neither half is a measure of them, because both also
    // hold Windows' own work. The only per-program figure anywhere in this data
    // is BootOffender.DegradationMs.
    int? MainPathMs,

    // The programs Windows blamed for this boot, worst first.
    //
    // Never a page or a prefix: nothing here is cut to fit a caller's count,
    // which is the one failure a flat "recent offenders" call could not avoid.
    //
    // But this is best effort, not a guarantee of completeness. A record
    // Windows wrote without a name, a delay, or a boot to attach it to is
    // dropped rather than guessed at, and one that will not read is skipped —
    // losing a name beats inventing or misattributing one. So this can be
    // short of what Windows logged without being able to say so.
    //
    // Which makes it safe to say "Windows blamed these three" and never "only
    // these three" or "these three are all of them".
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
