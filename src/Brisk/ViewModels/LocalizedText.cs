using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.Localization;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// One resolver for the engine's "English prose + stable key + args"
/// convention: render in the user's language when the key exists, fall
/// back to the engine's English when it does not. FindingRow used to own
/// the evidence half privately; the revelation band needs the same rules,
/// so it lives here once.
public static class LocalizedText
{
    public static string Evidence(DiagnosticFinding finding, Loc loc) =>
        finding.EvidenceKey is { } key
            ? Resolve(key, finding.EvidenceArgs, finding.Evidence, loc)
            : finding.Evidence;

    public static (string Value, string Caption) Headline(Headline headline, Loc loc) => (
        Resolve(headline.ValueKey, headline.ValueArgs, headline.Value, loc),
        Resolve(headline.CaptionKey, headline.CaptionArgs, headline.Caption, loc));

    private static string Resolve(string key, IReadOnlyList<string>? args,
        string english, Loc loc)
    {
        var template = loc[key];   // the indexer returns the key when missing
        if (string.Equals(template, key, StringComparison.Ordinal)) return english;
        return loc.F(key, (args ?? Array.Empty<string>()).Cast<object>().ToArray());
    }
}
