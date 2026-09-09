using System;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// The rule that names the likeliest reason a "very slow" PC scores well:
/// Windows lives on a spinning disk. It exists because the second outside
/// tester's machine (2026-09-09) was slow by its owner's account and brisk
/// had nothing to say about why — every finding it owns is a setting or a
/// timing, and the disk under all of them was not a thing brisk looked at.
public class SystemDiskHddRuleTests
{
    private static DiagnosticContext With(DiskMedium medium)
    {
        var disk = new FakeDisk { Medium = medium };
        return TestContext.Empty() with { Disk = disk };
    }

    [Fact]
    public void SpinningSystemDisk_IsAMeasuredWarningNotice_ThatCosts12Points()
    {
        var finding = new SystemDiskHddRule().Detect(With(DiskMedium.Rotational));

        Assert.NotNull(finding);
        Assert.Equal("system-disk-hdd", finding!.RuleId);
        Assert.Equal(RuleCategory.Advise, finding.Category);
        Assert.False(finding.CanFix);
        // A notice: brisk will not swap a drive. A MEASURED one: the drive
        // itself reported the seek penalty, and a gauge that read 89 over a
        // spinning system disk would be the same lie the 106 s boot told.
        Assert.Equal(FindingKind.Notice, finding.Kind);
        Assert.Equal(ImpactClass.Measured, finding.ImpactClass);
        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal(4, finding.ImpactStars);
        // Warning x 4 stars = 12. The boot rule charges the symptom; this
        // one names the cause. Both at Critical would take 50 for one disk.
        Assert.Equal(88, HealthScore.Compute(new[] { finding }));
    }

    [Fact]
    public void SolidStateSystemDisk_IsNotAFinding()
    {
        Assert.Null(new SystemDiskHddRule().Detect(With(DiskMedium.Solid)));
    }

    // A volume brisk could not map to one disk, a bridge that answered
    // nothing, a VM: no reading, no verdict. brisk never grades on a guess.
    [Fact]
    public void UnknownMedium_IsNotAFinding()
    {
        Assert.Null(new SystemDiskHddRule().Detect(With(DiskMedium.Unknown)));
    }

    [Fact]
    public void AsksAboutTheDiskWindowsRunsFrom_NotAFixedLetter()
    {
        var disk = new FakeDisk { Medium = DiskMedium.Rotational };
        new SystemDiskHddRule().Detect(TestContext.Empty() with { Disk = disk });

        Assert.Equal(Path.GetPathRoot(Environment.SystemDirectory), disk.AskedRoot);
    }

    [Fact]
    public void Evidence_NamesTheSsd_PointsAtTaskManager_AndPromisesNothing()
    {
        var finding = new SystemDiskHddRule().Detect(With(DiskMedium.Rotational))!;

        Assert.Contains("SSD", finding.Evidence);
        Assert.Contains("Task Manager", finding.Evidence);
        Assert.Contains("spinning", finding.Evidence);
        // No cleanup or switch is offered as a substitute for the drive.
        Assert.DoesNotContain("faster", finding.Evidence);
        Assert.Equal("rule.system-disk-hdd.evidence", finding.EvidenceKey);
        var letter = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        Assert.Equal(new[] { letter }, finding.EvidenceArgs);
        Assert.Null(finding.Headline);   // there is no number to lead with
    }

    [Fact]
    public void Registered_ExactlyOnce()
    {
        Assert.Equal(1, DiagnosticRuleRegistry.All.Count(r => r.Id == "system-disk-hdd"));
    }
}
