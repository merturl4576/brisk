using Brisk.Localization;
using Brisk.ViewModels;
using BriskEngine.Models;
using Xunit;

namespace Brisk.Tests;

/// The GUI edge of the English-leak fix: the engine ships stable
/// EvidenceKey + args next to its English prose, and FindingRow /
/// TargetRow rebuild the sentence from the resx in the user's language.
public class EvidenceLocalizationTests
{
    private static Loc Loc(string lang)
    {
        var loc = new Loc();
        loc.SetLanguage(lang);
        return loc;
    }

    private static FindingRow Row(DiagnosticFinding finding, Loc loc) =>
        new(finding, loc, canUndo: false, _ => { }, _ => { });

    [Fact]
    public void Evidence_WithKnownKey_SpeaksTheUsersLanguage()
    {
        var finding = TestData.Finding("startup-bloat", cat: RuleCategory.Confirm,
            evidenceKey: "rule.startup-bloat.evidence.heavy",
            evidenceArgs: new[] { "7", "Discord, Steam" });

        var tr = Row(finding, Loc("tr"));
        Assert.Equal("Windows ile birlikte 7 program başlıyor. " +
            "Gerektiğinde elle açılabilecek ağır olanlar: Discord, Steam.", tr.Evidence);

        var en = Row(finding, Loc("en"));
        Assert.Equal("7 programs start with Windows. " +
            "Heavy ones that can be started manually instead: Discord, Steam.",
            en.Evidence);
    }

    [Fact]
    public void Evidence_UnknownKey_FallsBackToEngineProse()
    {
        var finding = TestData.Finding("custom-x",
            evidenceKey: "rule.custom-x.evidence", evidenceArgs: new[] { "data" });
        Assert.Equal("Evidence custom-x", Row(finding, Loc("tr")).Evidence);
    }

    [Fact]
    public void Evidence_NoKey_KeepsEngineProse()
    {
        var finding = TestData.Finding("disk-breakdown", cat: RuleCategory.Advise,
            canFix: false);
        Assert.Equal("Evidence disk-breakdown", Row(finding, Loc("tr")).Evidence);
    }

    [Fact]
    public void TargetRow_LocalizesNameAndSkipReason_FromStableData()
    {
        var scan = TestData.Target("chrome-cache", CleanupLevel.Safe, 0,
            skipped: "chrome is running — close it to include this target",
            app: "chrome");
        var row = new TargetRow(scan, Loc("tr"));

        Assert.Equal("Chrome önbelleği", row.DisplayName);
        Assert.Equal("chrome açık — dahil etmek için önce kapat", row.SkippedText);
        Assert.False(row.IsSelectable);   // skip semantics untouched
    }

    [Fact]
    public void TargetRow_UnknownTarget_FallsBackToEngineName_GenericSkip()
    {
        var scan = TestData.Target("future-target", CleanupLevel.Safe, 0,
            skipped: "some engine reason");
        var row = new TargetRow(scan, Loc("tr"));

        Assert.Equal("future-target", row.DisplayName);   // engine DisplayName
        Assert.Equal(Loc("tr")["clean.skipped"], row.SkippedText);
    }

    [Fact]
    public void TargetRow_NotSkipped_HasEmptySkipText()
    {
        var row = new TargetRow(
            TestData.Target("user-temp", CleanupLevel.Safe, 2048), Loc("en"));
        Assert.Equal("", row.SkippedText);
        Assert.Equal("User temp files", row.DisplayName);
    }
}
