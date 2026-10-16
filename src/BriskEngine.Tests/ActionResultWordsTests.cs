using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionResultWordsTests
{
    private static RowResult R(RowOutcome o, string? reason = null, ReadBackState? rb = null) =>
        new("x", o, reason, rb, 0, 0);

    [Theory]
    [InlineData(RowOutcome.PendingRestart, null, null, "action.result.pending", ResultTone.Muted)]
    [InlineData(RowOutcome.Skipped, ActionRunner.NeedsAdmin, null, "action.result.needs-admin", ResultTone.Muted)]
    [InlineData(RowOutcome.Skipped, "nothing to clean", null, "action.result.skipped", ResultTone.Muted)]
    [InlineData(RowOutcome.Failed, "boom", null, "action.result.failed", ResultTone.Bad)]
    [InlineData(RowOutcome.Done, null, ReadBackState.Reverted, "action.result.reverted", ResultTone.Warn)]
    [InlineData(RowOutcome.Done, null, ReadBackState.WrittenButIgnored, "action.result.ignored", ResultTone.Warn)]
    [InlineData(RowOutcome.Done, null, ReadBackState.WrittenButUnverified, "action.result.written", ResultTone.Good)]
    [InlineData(RowOutcome.Done, null, null, "action.result.done", ResultTone.Good)]
    public void For_MapsOutcomeToKeyAndTone(RowOutcome o, string? reason, ReadBackState? rb, string key, ResultTone tone)
    {
        var w = ActionResultWords.For(ActionGroupKind.Space, R(o, reason, rb));
        Assert.Equal(key, w.Key);
        Assert.Equal(tone, w.Tone);
        Assert.Equal(ActionStrings.En(key), w.English);
    }

    [Fact]
    public void For_PrivacyDone_SaysOff() =>
        Assert.Equal("action.result.off", ActionResultWords.For(ActionGroupKind.Privacy, R(RowOutcome.Done)).Key);

    [Fact]
    public void For_KeepsTheReasonOnlyForSkippedAndFailed()
    {
        Assert.Equal("boom", ActionResultWords.For(null, R(RowOutcome.Failed, "boom")).Reason);
        Assert.Null(ActionResultWords.For(null, R(RowOutcome.Skipped, ActionRunner.NeedsAdmin)).Reason);
        Assert.Null(ActionResultWords.For(null, R(RowOutcome.Done)).Reason);
    }
}
