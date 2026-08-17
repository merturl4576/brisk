using System.Collections.Generic;

namespace BriskEngine.Models;

/// RequiresAppClosedProcess may name SEVERAL process-name candidates,
/// '|'-separated ("WhatsApp|WhatsApp.Root"): one app, many possible process
/// names across versions. The first candidate is the human-facing app name.
/// (The 2026-08-17 live incident: modern WhatsApp Desktop runs as
/// "WhatsApp.Root", so an exact match on "WhatsApp" silently never fired
/// and a 310 MB locked cache landed in the promise.)
public sealed record CleanupTarget(
    string Id,
    string DisplayName,
    CleanupLevel Level,
    IReadOnlyList<string> PathTemplates,
    string Category,
    bool DeletesContentsNotDirectory = false,
    bool Regenerates = false,
    string? RequiresAppClosedProcess = null,
    bool RequiresIndividualSelection = false,
    bool RequiresExplicitOptIn = false,
    bool BypassesRecycleBin = false,
    bool RequiresElevation = false)
{
    /// Every process name that counts as "this app is running". Trimmed
    /// and empties dropped (review round 1): a bare Split would let a
    /// future registry edit like "WhatsApp | WhatsApp.Root" produce
    /// candidates with spaces that Process.GetProcessesByName never
    /// matches — silently reviving the exact bug this field exists to fix.
    public IReadOnlyList<string> AppProcessCandidates =>
        RequiresAppClosedProcess is { } app
            ? app.Split('|', System.StringSplitOptions.RemoveEmptyEntries
                           | System.StringSplitOptions.TrimEntries)
            : System.Array.Empty<string>();

    /// The name the GUI shows for this app ("WhatsApp", never "WhatsApp.Root").
    public string? AppDisplayName =>
        AppProcessCandidates is { Count: > 0 } candidates ? candidates[0] : null;
}
