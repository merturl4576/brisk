using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests;

/// The live workbench (2026-09-01) read "PyCharm Community Edition" as proof
/// that Unity was installed, so a 600 MB orphaned Unity folder went unreported.
public class OrphanedDataRuleTests
{
    [Theory]
    [InlineData("PyCharm Community Edition 2024.3", "Unity", false)]   // the live false positive
    [InlineData("Unity Hub", "Unity", true)]
    [InlineData("unity 2022.3.1f1", "Unity", true)]
    [InlineData("Docker Desktop", "Docker Desktop", true)]
    [InlineData("Docker Desktop 4.30", "Docker", true)]
    [InlineData("JetBrains dotPeek 2025.3", "JetBrains", true)]
    [InlineData("BlueStacks Services", "BlueStacks", true)]
    [InlineData("Immunity Debugger", "Unity", false)]
    public void NameMatches_requires_a_whole_word(string displayName, string tool, bool expected)
        => Assert.Equal(expected, OrphanedDataRule.NameMatches(displayName, tool));
}
