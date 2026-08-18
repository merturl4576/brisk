using System;

namespace BriskEngine.Diagnostics;

/// One entry from Windows' own boot performance log (ID 100). Windows measures
/// this itself, which is what makes it stronger than any heuristic brisk could
/// invent. The documented PostBootTime and BootDegradationTime fields do not
/// exist under those names on Windows 11 26100 — the payload calls them
/// BootPostBootTime and BootDegradationDelta — so nothing here depends on them.
public sealed record BootRecord(DateTime When, int BootMs, int MainPathMs);

/// One thing Windows blamed for slowing a boot (ID 101). DegradationMs is the
/// part Windows attributes to this program beyond what it expected.
public sealed record BootOffender(
    DateTime When,
    string Name,          // "Spotify.exe"
    string FriendlyName,  // "Spotify" — may be empty
    string Path,
    int DegradationMs);
