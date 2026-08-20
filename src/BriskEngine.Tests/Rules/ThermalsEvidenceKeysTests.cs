using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

/// The gap that let two new evidence variants ship untranslated.
///
/// The whole 653-test suite passed with
/// rule.thermals.evidence.cpu-unread.integrity-on missing from both resx
/// files: the rule tests pin the key the rule emits, the loc tests pin that
/// the two resx files agree with EACH OTHER, and nothing joined the two. A key
/// absent from both files is absent from both consistently, so it slipped
/// between them — and the GUI answers a missing key with the raw key, which is
/// what a Turkish reader would have seen.
///
/// So this drives the rule itself through every state that changes its
/// EvidenceKey and checks the resx for whatever comes back. A variant added
/// later is covered without anyone remembering to list it here, which is the
/// property the InlineData version of this test would not have had.
public class ThermalsEvidenceKeysTests
{
    private static DiagnosticContext Hot(bool cpu, bool gpu, bool? memoryIntegrity)
    {
        var ctx = TestContext.Empty();
        var sensors = (FakeSensors)ctx.Sensors;
        if (cpu) sensors.CpuTemp = 88;
        if (gpu) sensors.GpuTemp = 78;
        ((FakeMemoryIntegrity)ctx.MemoryIntegrity).On = memoryIntegrity;
        return ctx;
    }

    /// Every combination that produces a finding at all. The both-unread case
    /// is missing on purpose: no reading means no finding.
    public static IEnumerable<object[]> States() => new[]
    {
        new object[] { true,  true,  null! },
        new object[] { true,  false, null! },
        new object[] { false, true,  null! },
        new object[] { false, true,  true },
        new object[] { false, true,  false },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void EveryEvidenceKeyTheRuleEmits_ExistsInBothResxFiles(
        bool cpu, bool gpu, bool? memoryIntegrity)
    {
        var finding = new ThermalsRule().Detect(Hot(cpu, gpu, memoryIntegrity));
        Assert.NotNull(finding);
        var key = finding!.EvidenceKey;
        Assert.NotNull(key);

        foreach (var file in new[] { "Strings.resx", "Strings.tr.resx" })
            Assert.True(Keys(file).Contains(key!),
                $"{file} has no '{key}' — the GUI would print the raw key.");
    }

    private static HashSet<string> Keys(string fileName) =>
        XDocument.Load(Path.Combine(LocalizationDir(), fileName)).Root!
            .Elements("data")
            .Select(e => (string)e.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);

    private static string LocalizationDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null;
             dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, "src", "Brisk", "Localization");
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }
}
