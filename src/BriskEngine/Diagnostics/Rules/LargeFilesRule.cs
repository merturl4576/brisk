using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BriskEngine.Models;
using BriskEngine.Paths;

namespace BriskEngine.Diagnostics.Rules;

/// The biggest files in the user's profile, by name — and nothing else.
///
/// WHY IT EXISTS. disk-breakdown reports folder totals, and a field test on
/// a neglected machine plus a live look at the maintainer's own showed what
/// that buys: "Desktop: 58.8 GB (over threshold)", with a 23.5 GB VM disk, a
/// 7.6 GB ISO and a 3.65 GB archive sitting in it unnamed. Nobody feels a
/// folder total. Everybody feels a named 23.5 GB file, because a name is
/// what a decision can be made about.
///
/// WHAT IT WILL NEVER DO. It has no fix, no button and no delete. These
/// files are the user's — an ISO he installed from, a VM disk he might still
/// start, a video nobody else can judge — and brisk cannot tell a dead one
/// from a live one from here. Notice rather than Problem for exactly that
/// reason: a score that charged for them would be brisk grading a machine
/// for holding its owner's work. CanFix is false and Category is Advise,
/// which is the consent level FixRunner refuses to apply a fix for at all,
/// so "reports and never touches" is a property of the build.
///
/// THE PATHS ARE RELATIVE TO THE PROFILE, and that is not cosmetic. Evidence
/// travels onto surfaces built to be read by other people, and
/// %USERPROFILE% carries the user's name in it. A file outside the profile
/// keeps its full path — shortening it against a directory it does not sit
/// under would produce a "..\..\" walk that names nothing — and the roots
/// below are all inside the profile, so that branch only fires for something
/// reached through a link.
///
/// THE HEADLINE CAPTION NAMES NOTHING. The report card is a picture people
/// post, it takes the headline, and PrivacyRedLineTests bans names and paths
/// from it. The caption is therefore a sentence about a file rather than a
/// sentence containing one, and the size is the only thing that travels.
///
/// IT WALKS NOTHING ITSELF. Every root goes through FileStats.Of, the
/// per-scan memo, on the same keys disk-breakdown uses for its four — so on
/// a scan that runs both, this rule costs the four folders disk-breakdown
/// does not already measure and no more.
public sealed class LargeFilesRule : AdviseRuleBase
{
    public override string Id => "large-files";

    /// Below this the rule says nothing at all. FileStats.MinFileBytes
    /// (500 MB) is what gets a file KEPT by the walk; this is what makes the
    /// kept list worth showing. A named 700 MB file is not a revelation, and
    /// a finding that fired on one would teach the reader to skip this rule
    /// before it ever showed him a 23.5 GB one.
    public const long MinLargestBytes = 1L << 30;

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var largest = new List<LargeFile>();
        foreach (var root in Roots())
            largest.AddRange(FileStats.Of(ctx, root).Largest);

        largest.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
        if (largest.Count == 0 || largest[0].Bytes < MinLargestBytes) return null;
        if (largest.Count > FileStats.Take)
            largest.RemoveRange(FileStats.Take, largest.Count - FileStats.Take);

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var lines = new List<string>(largest.Count);
        foreach (var file in largest)
            lines.Add($"{Fmt.Bytes(file.Bytes)}  {Shorten(file.Path, profile)}  " +
                $"({file.WriteUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)})");
        var list = string.Join("; ", lines);

        var top = Fmt.Bytes(largest[0].Bytes);
        return new DiagnosticFinding(
            Id, $"rule.{Id}.title",
            "The biggest files on this machine, by name",
            "Files of 500 MB or more in your profile, largest first — " +
            $"brisk names them and touches none: {list}",
            Severity.Info, Category, ImpactStars: 2, CanFix: false,
            FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { list },
            Headline: new Headline(
                top, "the largest single file in your profile",
                $"rule.{Id}.headline.value", new[] { top },
                $"rule.{Id}.headline.caption", Array.Empty<string>()),
            // A fact brisk reports and cannot act on — the same standing the
            // privacy disclosures have, and for a stronger reason: brisk
            // could act here and refuses to.
            Kind: FindingKind.Notice);
    }

    /// Where a big file is worth naming. The user's own folders, plus the two
    /// AppData trees, because a 20 GB Docker image or a stale Electron cache
    /// is exactly the kind of thing nobody knows they are keeping.
    ///
    /// THE FIRST FOUR ARE SPELLED THE WAY DiskBreakdownRule SPELLS THEM —
    /// Desktop through GetFolderPath, Downloads and the AppData pair through
    /// PathExpander — because the memo is keyed by the path string. A root
    /// written the other way would walk the same folder a second time under
    /// a second key, which is the one cost this rule was designed not to
    /// have.
    ///
    /// Deduped, because a redirected profile can make two of these the same
    /// directory and a folder listed twice would name its files twice.
    private static IEnumerable<string> Roots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            PathExpander.Expand(@"%USERPROFILE%\Downloads"),
            PathExpander.Expand("%LOCALAPPDATA%"),
            PathExpander.Expand("%APPDATA%"),
            PathExpander.Expand(@"%USERPROFILE%\Documents"),
            PathExpander.Expand(@"%USERPROFILE%\Videos"),
            PathExpander.Expand(@"%USERPROFILE%\Pictures"),
            PathExpander.Expand(@"%USERPROFILE%\Music"),
        })
        {
            // Expand answers null for a variable this machine does not
            // define, and GetFolderPath answers "" for a folder it cannot
            // resolve. Both mean the same thing here: no root, no walk.
            if (!string.IsNullOrEmpty(root) && seen.Add(root)) yield return root;
        }
    }

    /// The path as the reader should see it: relative to the profile when it
    /// sits under it, whole when it does not.
    private static string Shorten(string path, string profile)
    {
        if (string.IsNullOrEmpty(profile)) return path;
        var prefix = profile.EndsWith(Path.DirectorySeparatorChar)
            ? profile : profile + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? path[prefix.Length..] : path;
    }
}
