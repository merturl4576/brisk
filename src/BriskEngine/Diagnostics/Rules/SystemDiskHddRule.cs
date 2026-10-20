using System;
using System.IO;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

/// Windows lives on a spinning disk. The likeliest reason a PC its owner
/// calls "very slow" scored well on 2026-09-09: every finding brisk owned
/// was a setting or a timing, and the disk under all of them was not a
/// thing brisk looked at. The owner's verdict was "your system is wrong",
/// and about the number he was right.
///
/// Advise, and a Notice: brisk will not swap a drive, so there is no button.
/// But MEASURED, so it charges the score — the drive itself reported the
/// seek penalty, and a gauge that read 89 over it would be the same lie the
/// free 106 s boot told. Warning, four stars: 12 points. The boot rule
/// charges the symptom on its own scale; this one names the cause, and the
/// two are never joined — nothing here claims how many boot seconds the
/// disk is worth, because nothing measured that.
public sealed class SystemDiskHddRule : AdviseRuleBase
{
    public override string Id => "system-disk-hdd";

    /// The drive Windows actually runs from, not a fixed C:. A machine that
    /// boots from D: is unusual but real, and a verdict on the wrong disk
    /// would be a wrong verdict.
    internal static string SystemRoot =>
        Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var root = SystemRoot;
        // Unknown is silent on purpose: no reading, no verdict.
        if (ctx.Disk.MediumOf(root) != DiskMedium.Rotational) return null;

        var letter = root.TrimEnd('\\');
        return new DiagnosticFinding(Id, $"rule.{Id}.title",
            "Windows runs from a spinning disk",
            $"The disk Windows runs from ({letter}) is a spinning drive: it reports a " +
            "seek penalty, the same reading Task Manager uses to label a disk HDD on " +
            "its Performance page. Everything on this machine waits on that disk — " +
            "starting up, opening programs, updates — and nothing brisk cleans or " +
            "switches changes that. An SSD in its place is the one upgrade that would " +
            "make this PC feel new. This finding charges the score because the " +
            "slowness is measured, not because brisk can fix it.",
            Severity.Warning, Category, ImpactStars: 4, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence", EvidenceArgs: new[] { letter },
            Kind: FindingKind.Notice, ImpactClass: ImpactClass.Measured);
    }
}
