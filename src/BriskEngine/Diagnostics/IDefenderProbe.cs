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

/// One row of Security Center's antivirus table — the list the Windows
/// Security app shows under "Virus & threat protection". IsDefender marks
/// Microsoft's own entry, so a rule can ask "who else holds the job".
public sealed record AntivirusProduct(
    string Name, bool IsDefender, bool Enabled, bool DefinitionsOutdated);

/// Security Center's productState, decoded. Three bytes: product type,
/// then the on/off byte (0x10 set = on), then the signature byte (0x10 set
/// = out of date). Decoded the way scripts across the industry decode it,
/// and pinned against the value this machine's Defender reported live.
public static class AntivirusState
{
    public static (bool Enabled, bool Outdated) Decode(int productState) =>
        ((productState & 0x1000) != 0, (productState & 0x10) != 0);

    /// Defender by its registration path first — "windowsdefender://", not
    /// a thing a third party would claim — and by its name second, for a
    /// Windows that lists it under a localized name with the path missing.
    public static bool IsDefender(string name, string path) =>
        path.StartsWith("windowsdefender:", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Defender", StringComparison.OrdinalIgnoreCase);
}

public interface IDefenderProbe
{
    DefenderStatus? Read();

    /// Every antivirus Security Center lists, Defender included. Empty when
    /// the table could not be read — Server editions have no Security
    /// Center — and empty always means "brisk could not see", never "there
    /// is none": a Windows with no antivirus row is not a thing.
    IReadOnlyList<AntivirusProduct> Registered();
}
