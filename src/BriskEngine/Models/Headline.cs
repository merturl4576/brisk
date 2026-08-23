namespace BriskEngine.Models;

/// The one number a finding leads with, and what that number is. English
/// prose plus stable key + args — exactly the evidence convention on
/// DiagnosticFinding: a consumer without a resource table reads
/// Value/Caption, a GUI rebuilds both in the user's language.
public sealed record Headline(
    string Value,                       // formatted, English units: "57.7 GB"
    string Caption,                     // English: "Desktop — the largest measured folder"
    string ValueKey,
    IReadOnlyList<string> ValueArgs,
    string CaptionKey,
    IReadOnlyList<string> CaptionArgs);
