using System;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics;

public sealed record FixOutcome(bool Ok, string Message);

public sealed class FixRunner
{
    private readonly FixJournal _journal;
    private readonly ActionLog _log;

    public FixRunner(FixJournal journal, ActionLog log)
    {
        _journal = journal;
        _log = log;
    }

    public FixOutcome Apply(IDiagnosticRule rule, DiagnosticContext ctx)
    {
        if (rule.Category == RuleCategory.Advise)
            return new FixOutcome(false, $"{rule.Id}: rule has no fix (advise-only)");
        try
        {
            var prior = rule.Fix(ctx);
            _journal.RecordFix(rule.Id, prior);
            _log.Append(new { ts = DateTime.UtcNow, ruleId = rule.Id, action = "fix" });
            return new FixOutcome(true, $"{rule.Id}: fixed");
        }
        catch (Exception ex)
        {
            return new FixOutcome(false, $"{rule.Id}: fix failed — {ex.Message}");
        }
    }

    public FixOutcome Undo(IDiagnosticRule rule, DiagnosticContext ctx)
    {
        try
        {
            var prior = _journal.LastUndoablePriorState(rule.Id);
            if (prior is null)
                return new FixOutcome(false, $"{rule.Id}: nothing to undo");
            rule.Undo(ctx, prior);
            _journal.RecordUndo(rule.Id);
            _log.Append(new { ts = DateTime.UtcNow, ruleId = rule.Id, action = "undo" });
            return new FixOutcome(true, $"{rule.Id}: undone");
        }
        catch (Exception ex)
        {
            return new FixOutcome(false, $"{rule.Id}: undo failed — {ex.Message}");
        }
    }
}
