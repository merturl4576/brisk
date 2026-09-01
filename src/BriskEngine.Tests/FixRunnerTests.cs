using System;
using System.IO;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class ToggleRule : IDiagnosticRule
{
    public string State = "bad";
    public string Id => "toggle";
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => State == "bad"
        ? new DiagnosticFinding(Id, "rule.toggle.title", "Toggle is bad", $"State: {State}",
            Severity.Warning, Category, 3, true, "Set state to good")
        : null;
    public string Fix(DiagnosticContext ctx) { var prior = State; State = "good"; return prior; }
    public void Undo(DiagnosticContext ctx, string prior) => State = prior;
}

file sealed class AdviseRule : IDiagnosticRule
{
    public string Id => "advise-only";
    public RuleCategory Category => RuleCategory.Advise;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => null;
    public string Fix(DiagnosticContext ctx) => throw new InvalidOperationException();
    public void Undo(DiagnosticContext ctx, string prior) => throw new InvalidOperationException();
}

/// The unelevated CLI handed the user .NET's own sentence — "Access to the
/// registry key 'HKEY_LOCAL_MACHINE\…' is denied." A refusal for want of
/// rights has a plain name and a way out; this rule stands in for one.
file sealed class RefusingRule : IDiagnosticRule
{
    public string Id => "refusing";
    public RuleCategory Category => RuleCategory.Auto;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => null;
    public string Fix(DiagnosticContext ctx) =>
        throw new UnauthorizedAccessException(
            @"Access to the registry key 'HKEY_LOCAL_MACHINE\X' is denied.");
    public void Undo(DiagnosticContext ctx, string prior) => throw new InvalidOperationException();
}

public sealed class FixRunnerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-fr-").FullName;
    private readonly DiagnosticContext _ctx = TestContext.Empty();
    private FixRunner Runner() => new(
        new FixJournal(Path.Combine(_root, "j.jsonl")),
        new ActionLog(Path.Combine(_root, "log.jsonl")));

    [Fact]
    public void ApplyThenUndo_RestoresState()
    {
        var rule = new ToggleRule();
        var runner = Runner();
        Assert.True(runner.Apply(rule, _ctx).Ok);
        Assert.Equal("good", rule.State);
        Assert.True(runner.Undo(rule, _ctx).Ok);
        Assert.Equal("bad", rule.State);
    }

    [Fact]
    public void UndoWithoutFix_Fails()
    {
        Assert.False(Runner().Undo(new ToggleRule(), _ctx).Ok);
    }

    [Fact]
    public void AdviseRule_IsNeverApplied()
    {
        Assert.False(Runner().Apply(new AdviseRule(), _ctx).Ok);
    }

    [Fact]
    public void Undo_WithCorruptJournal_ReturnsFailedOutcome_NotThrow()
    {
        var journalPath = Path.Combine(_root, "corrupt.jsonl");
        // Write only garbage lines
        File.WriteAllText(journalPath, "not-json{{{\n");
        File.AppendAllText(journalPath, "{invalid json\n");
        File.AppendAllText(journalPath, "\n");

        var runner = new FixRunner(
            new FixJournal(journalPath),
            new ActionLog(Path.Combine(_root, "log.jsonl")));

        var rule = new ToggleRule();
        // Should not throw, should return failed outcome
        var outcome = runner.Undo(rule, _ctx);
        Assert.False(outcome.Ok);
        Assert.Contains("nothing to undo", outcome.Message);
    }

    /// FIX WAVE, Finding 2. The return code of ChangeDisplaySettingsEx was
    /// discarded, so a driver refusing the mode (DISP_CHANGE_BADMODE — the
    /// cable or adapter that cannot carry the rate, which is the whole reason
    /// this rule has a countdown) still reported a fixed display. It must
    /// reach the user as a failure, and nothing may be journaled: an Undo
    /// offered for a change that never happened is the same lie twice.
    [Fact]
    public void DisplayFix_RefusedByTheDriver_FailsAndJournalsNothing()
    {
        var displays = new FakeDisplays();
        displays.Attached.Add(new DisplayInfo("DISPLAY1", "Dell U2720Q", 60, 144));
        displays.RefusedRates.Add(144);
        var journal = new FixJournal(Path.Combine(_root, "display.jsonl"));
        var runner = new FixRunner(journal,
            new ActionLog(Path.Combine(_root, "log.jsonl")));

        var outcome = runner.Apply(new BriskEngine.Diagnostics.Rules.DisplayRefreshRule(),
            TestContext.Empty() with { Displays = displays });

        Assert.False(outcome.Ok);
        Assert.Contains("refused", outcome.Message);
        Assert.Empty(journal.ListUndoable());
    }

    [Fact]
    public void Apply_names_the_missing_right_when_the_fix_is_refused()
    {
        var outcome = Runner().Apply(new RefusingRule(), _ctx);
        Assert.False(outcome.Ok);
        Assert.Contains("needs administrator rights", outcome.Message);
        Assert.DoesNotContain("HKEY_LOCAL_MACHINE", outcome.Message);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
