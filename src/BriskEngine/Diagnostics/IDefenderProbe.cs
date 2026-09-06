using System;
using System.Collections.Generic;

namespace BriskEngine.Diagnostics;

/// What Windows Defender says about itself, through WMI. Every member is
/// nullable on its own: a read that answered for real-time protection and
/// not for the exclusions reports exactly that. The whole record is null
/// when nothing could be read (no Defender, namespace missing, a throw).
///
/// ExclusionCount covers paths, extensions and processes, and so does
/// Exclusions: the list carries every entry the count counted — a path
/// bare, an extension as "extension: .vdi", a process as
/// "process: chrome.exe" — so the number the finding prints and the fold
/// the Privacy page opens can never describe different things. Count equals
/// the list's length wherever the list was read, and the list is for that
/// fold and for nothing else.
///
/// Windows hides the exclusion list from a process that is not
/// elevated, and it hides it as an ANSWER rather than as an error, so the
/// real probe reports ExclusionCount null when it is not elevated rather
/// than a count it did not read — see RealDefenderProbe for the shape that
/// answer actually takes, which is not the empty list it was assumed to be.
public sealed record DefenderStatus(
    bool? RealTimeOn,
    bool? TamperOn,
    string? RunningMode,
    int? ExclusionCount,
    IReadOnlyList<string> Exclusions,
    DateTime? LastQuickScanUtc,
    DateTime? LastFullScanUtc,
    int? SignatureAgeDays);

public interface IDefenderProbe
{
    DefenderStatus? Read();
}
