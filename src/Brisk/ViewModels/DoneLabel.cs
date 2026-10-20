using System;
using Brisk.Localization;

namespace Brisk.ViewModels;

/// The past-tense line for a fixed rule on the report card: the rule's own
/// "done" sentence when it has one, else "Fixed: {title}".
internal static class DoneLabel
{
    public static string For(Loc loc, string ruleId, string titleKey, string english)
    {
        var key = $"rule.{ruleId}.done";
        var text = loc[key];   // the indexer returns the key itself when missing
        return string.Equals(text, key, StringComparison.Ordinal)
            ? loc.F("overview.report.fixed", loc.Title(titleKey, english))
            : text;
    }
}
