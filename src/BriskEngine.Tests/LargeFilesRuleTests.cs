using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BriskEngine;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests;

/// The field test that ordered this rule: disk-breakdown said "Desktop:
/// 58.8 GB (over threshold)" on a neglected machine while a 23.5 GB VM
/// disk, a 7.6 GB ISO and a 3.65 GB archive sat in it unnamed. Nobody feels
/// a folder total. Everybody feels a named 23.5 GB file.
public class LargeFilesRuleTests
{
    private const long GB = 1L << 30;
    private const long MB = 1L << 20;

    private static string Profile =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string Downloads => PathExpander.Expand(@"%USERPROFILE%\Downloads")!;
    private static string Desktop =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    private static void Plant(DiagnosticContext ctx, string root,
        params LargeFile[] files)
    {
        var fake = (FakeFiles)ctx.Files;
        fake.Sizes[root] = files.Sum(f => f.Bytes);
        fake.LargeFiles[root] = files.ToList();
    }

    private static LargeFile File(string root, string relative, long bytes,
        int year = 2026, int month = 7, int day = 14) =>
        new(Path.Combine(root, relative), bytes,
            new DateTime(year, month, day, 9, 0, 0, DateTimeKind.Utc));

    /// The whole point, in one assertion set: the file is named, its size
    /// leads, and the name it is given carries no username.
    [Fact]
    public void AVmDiskOnTheDesktop_IsNamed_WithItsSizeAndItsDate()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads,
            File(Downloads, @"vms\workbench.vdi", (long)(23.5 * GB)));

        var finding = new LargeFilesRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal("large-files", finding!.RuleId);
        Assert.Equal("rule.large-files.title", finding.TitleKey);
        Assert.Equal("rule.large-files.evidence", finding.EvidenceKey);
        Assert.Contains(@"23.5 GB  Downloads\vms\workbench.vdi  (2026-07-14)",
            finding.Evidence);
        Assert.Equal(new[] { @"23.5 GB  Downloads\vms\workbench.vdi  (2026-07-14)" },
            finding.EvidenceArgs);

        var headline = finding.Headline;
        Assert.NotNull(headline);
        Assert.Equal(Fmt.Bytes((long)(23.5 * GB)), headline!.Value);
        Assert.Equal("rule.large-files.headline.value", headline.ValueKey);
        Assert.Equal(new[] { "23.5 GB" }, headline.ValueArgs);
    }

    /// THE PROFILE PATH NEVER REACHES THE TEXT. A relative path is not a
    /// nicety here — %USERPROFILE% carries the user's name, and evidence
    /// travels to surfaces built to be read by other people.
    [Fact]
    public void TheProfilePath_NeverReachesTheEvidence()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, File(Downloads, "big.iso", 8 * GB));

        var finding = new LargeFilesRule().Detect(ctx)!;

        Assert.DoesNotContain(Profile, finding.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Environment.UserName, finding.Evidence,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("big.iso", finding.Evidence);
    }

    /// The caption goes onto the report card, which is a picture people
    /// post. PrivacyRedLineTests bans names and paths there; this holds the
    /// same line at the rule, where the caption is written.
    [Fact]
    public void TheHeadlineCaption_NamesNoFile_AndNoPath()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, File(Downloads, @"vms\workbench.vdi", 23 * GB));

        var headline = new LargeFilesRule().Detect(ctx)!.Headline!;

        Assert.Equal("the largest single file in your profile", headline.Caption);
        Assert.DoesNotContain("workbench", headline.Caption,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\", headline.Caption, StringComparison.Ordinal);
        Assert.DoesNotContain("Downloads", headline.Caption,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("rule.large-files.headline.caption", headline.CaptionKey);
        Assert.Empty(headline.CaptionArgs);
    }

    /// A named 700 MB file is not a revelation, and a finding that fired on
    /// one would train the reader to skip this rule. One gigabyte is the
    /// floor for saying anything at all.
    [Fact]
    public void UnderAGigabyte_TheRuleSaysNothing()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, File(Downloads, "installer.exe", 900 * MB));

        Assert.Null(new LargeFilesRule().Detect(ctx));
    }

    [Fact]
    public void NothingBigAnywhere_IsSilence()
    {
        Assert.Null(new LargeFilesRule().Detect(TestContext.Empty()));
    }

    /// Exactly at the gigabyte the rule speaks — the boundary is stated
    /// rather than left to whichever comparison somebody typed.
    [Fact]
    public void ExactlyAGigabyte_Speaks()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, File(Downloads, "one.bin", GB));

        Assert.NotNull(new LargeFilesRule().Detect(ctx));
    }

    /// The roots are merged into ONE list, biggest first — a per-folder list
    /// would put a 2 GB file above a 20 GB one because it came from an
    /// earlier folder.
    [Fact]
    public void TheListsFromEveryRoot_MergeIntoOneOrderedList_CutAtTen()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, Enumerable.Range(1, 6)
            .Select(i => File(Downloads, $"d{i}.bin", i * GB)).ToArray());
        Plant(ctx, Desktop, Enumerable.Range(1, 6)
            .Select(i => File(Desktop, $"k{i}.bin", (i * GB) + (100 * MB))).ToArray());

        var finding = new LargeFilesRule().Detect(ctx)!;
        var lines = finding.EvidenceArgs![0].Split("; ");

        Assert.Equal(FileStats.Take, lines.Length);
        Assert.Equal(10, lines.Length);
        // Interleaved by size: k6, d6, k5, d5, ... — the desktop files are
        // each 100 MB heavier than the download file of the same index.
        Assert.StartsWith("6.1 GB  ", lines[0]);
        Assert.Contains("k6.bin", lines[0]);
        Assert.Contains("d6.bin", lines[1]);
        Assert.Contains("k5.bin", lines[2]);
        Assert.Equal(Fmt.Bytes((6 * GB) + (100 * MB)), finding.Headline!.Value);
        // The smallest survivor is the tenth biggest of the twelve.
        Assert.Contains("d2.bin", lines[9]);
    }

    /// Notice, not Problem: these files are the user's, brisk names them and
    /// touches none. A Problem would charge the health score for a VM disk
    /// somebody needs.
    [Fact]
    public void ItIsANotice_WithNoFixAndNoButton()
    {
        var ctx = TestContext.Empty();
        Plant(ctx, Downloads, File(Downloads, "vm.vdi", 23 * GB));

        var finding = new LargeFilesRule().Detect(ctx)!;

        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.False(finding.CanFix);
        Assert.Null(finding.FixDescription);
        Assert.Equal(RuleCategory.Advise, finding.Category);
        Assert.Equal(Severity.Info, finding.Severity);
        Assert.Equal(2, finding.ImpactStars);
    }

    /// A path outside the profile keeps its full self — shortening it
    /// against a profile it does not sit under would produce a walk of
    /// "..\..\" that names nothing.
    [Fact]
    public void APathOutsideTheProfile_IsPrintedWhole()
    {
        var ctx = TestContext.Empty();
        var outside = @"D:\archive";
        var fake = (FakeFiles)ctx.Files;
        fake.Sizes[Downloads] = 4 * GB;
        fake.LargeFiles[Downloads] = new List<LargeFile>
        {
            new(Path.Combine(outside, "old.7z"), 4 * GB,
                new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)),
        };

        var finding = new LargeFilesRule().Detect(ctx)!;

        Assert.Contains(@"4.0 GB  D:\archive\old.7z  (2026-01-02)", finding.Evidence);
    }

    /// THE WALK IS SHARED, and this is the assertion that says so: eight
    /// roots, eight walks, and disk-breakdown's four of them are already
    /// paid for when it runs first.
    [Fact]
    public void ItWalksEachRootOnce_AndReusesWhatDiskBreakdownAlreadyWalked()
    {
        var ctx = TestContext.Empty();
        var fake = (FakeFiles)ctx.Files;
        Plant(ctx, Downloads, File(Downloads, "vm.vdi", 23 * GB));

        new DiskBreakdownRule().Detect(ctx);
        var afterBreakdown = fake.StatsCalls;
        new LargeFilesRule().Detect(ctx);

        Assert.Equal(4, afterBreakdown);          // Local, Roaming, Desktop, Downloads
        Assert.Equal(8, fake.StatsCalls);         // the four new roots, and no more
        new LargeFilesRule().Detect(ctx);
        Assert.Equal(8, fake.StatsCalls);         // a second pass walks nothing
    }

    [Fact]
    public void TheRegistryShipsItExactlyOnce_RightAfterDiskBreakdown()
    {
        var ids = DiagnosticRuleRegistry.All.Select(r => r.Id).ToList();

        Assert.Single(ids.Where(id => id == "large-files"));
        Assert.Equal(ids.IndexOf("disk-breakdown") + 1, ids.IndexOf("large-files"));
    }
}
