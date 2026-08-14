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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
