using System;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;
using Xunit;

namespace BriskEngine.Tests;

file sealed class NonRegistrySwitch : IReadBackable
{
    public bool On;
    public WriteEffect Effect = WriteEffect.NotAPolicy;
    public string Id => "non-registry";
    public RuleCategory Category => RuleCategory.Auto;
    public bool IsOn(DiagnosticContext ctx) => On;
    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => Effect;
    public DiagnosticFinding? Detect(DiagnosticContext ctx) => null;
    public string Fix(DiagnosticContext ctx) { On = false; return "{}"; }
    public void Undo(DiagnosticContext ctx, string prior) => On = true;
}

public sealed class ReadBackContractTests
{
    [Fact]
    public void For_ReadsBackAnySwitch_NotOnlyTheRegistryFamily()
    {
        var rule = new NonRegistrySwitch { On = false };
        var rows = ReadBack.For(TestContext.Empty(),
            new[] { new UndoableFix("non-registry", DateTime.UtcNow) },
            new IDiagnosticRule[] { rule });
        Assert.Single(rows);
        Assert.Equal(ReadBackState.Held, rows[0].State);
    }

    [Theory]
    [InlineData(true, WriteEffect.NotAPolicy, ReadBackState.Reverted)]
    [InlineData(false, WriteEffect.Ignored, ReadBackState.WrittenButIgnored)]
    [InlineData(false, WriteEffect.Unread, ReadBackState.WrittenButUnverified)]
    [InlineData(false, WriteEffect.ActedOn, ReadBackState.Held)]
    public void StateOf_MapsEveryAnswer(bool on, WriteEffect effect, ReadBackState expected)
    {
        var rule = new NonRegistrySwitch { On = on, Effect = effect };
        Assert.Equal(expected, ReadBack.StateOf(TestContext.Empty(), rule));
    }

    [Fact]
    public void TheRegistryFamilyIsReadBackable()
    {
        Assert.True(typeof(IReadBackable).IsAssignableFrom(typeof(TelemetrySwitchRule)));
    }
}
