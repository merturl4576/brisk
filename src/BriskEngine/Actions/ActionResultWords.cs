using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

public enum ResultTone { Good, Warn, Muted, Bad }

public sealed record ResultWord(string Key, string English, string? Reason, ResultTone Tone);

/// The word a finished row gets: the same for the console and the window.
public static class ActionResultWords
{
    public static ResultWord For(ActionGroupKind? group, RowResult r)
    {
        static ResultWord W(string key, ResultTone tone, string? reason = null) =>
            new(key, ActionStrings.En(key), reason, tone);
        return r.Outcome switch
        {
            RowOutcome.Skipped when r.Reason == ActionRunner.NeedsAdmin => W("action.result.needs-admin", ResultTone.Muted),
            RowOutcome.Skipped => W("action.result.skipped", ResultTone.Muted, r.Reason),
            RowOutcome.Failed => W("action.result.failed", ResultTone.Bad, r.Reason),
            RowOutcome.PendingRestart => W("action.result.pending", ResultTone.Muted),
            _ => r.ReadBack switch
            {
                ReadBackState.Reverted => W("action.result.reverted", ResultTone.Warn),
                ReadBackState.WrittenButIgnored => W("action.result.ignored", ResultTone.Warn),
                ReadBackState.WrittenButUnverified => W("action.result.written", ResultTone.Good),
                _ => W(group == ActionGroupKind.Privacy ? "action.result.off" : "action.result.done", ResultTone.Good),
            },
        };
    }
}
