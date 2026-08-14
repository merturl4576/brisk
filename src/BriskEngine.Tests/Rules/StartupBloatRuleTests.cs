using System;
using System.Collections.Generic;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class StartupBloatRuleTests
{
    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeRegistry) Ctx(params string[] runItems)
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        foreach (var item in runItems) reg.SetString(RunKey, item, $@"C:\apps\{item}.exe");
        return (ctx, reg);
    }

    [Fact]
    public void HeavyStartupItem_IsAFinding()
    {
        var (ctx, _) = Ctx("Steam");
        var finding = new StartupBloatRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("Steam", finding!.Evidence);
    }

    [Fact]
    public void DisabledHeavyItem_NoFinding()
    {
        var (ctx, reg) = Ctx("Steam");
        reg.SetBytes(ApprovedKey, "Steam", new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void FewLightItems_NoFinding()
    {
        var (ctx, _) = Ctx("MyTool", "OtherTool");
        Assert.Null(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void ManyItems_IsAFinding_EvenWithoutHeavyOnes()
    {
        var (ctx, _) = Ctx("A", "B", "C", "D", "E", "F");
        Assert.NotNull(new StartupBloatRule().Detect(ctx));
    }

    [Fact]
    public void Fix_DisablesOnlyHeavyItems_AndUndoRestores()
    {
        var (ctx, reg) = Ctx("Steam", "MyTool");
        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Steam")![0]);
        Assert.Null(reg.GetBytes(ApprovedKey, "MyTool")); // untouched
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetBytes(ApprovedKey, "Steam")); // was absent before
    }

    [Fact]
    public void Fix_DeniedWrite_IsNotRecordedInPrior()
    {
        // One HKCU heavy item (writable) + one HKLM heavy item (denied)
        var (ctx, reg) = Ctx("Steam");
        const string HklmRunKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string HklmApprovedKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        reg.SetString(HklmRunKey, "Discord", @"C:\apps\Discord.exe");
        reg.DenyWriteKeys.Add(HklmApprovedKey);

        var rule = new StartupBloatRule();
        var prior = rule.Fix(ctx);

        // HKCU Steam should be disabled
        Assert.Equal(0x03, reg.GetBytes(ApprovedKey, "Steam")![0]);
        // HKLM Discord should NOT be disabled (write was denied)
        Assert.Null(reg.GetBytes(HklmApprovedKey, "Discord"));
        // Prior should only contain HKCU entry, not HKLM
        var priorMap = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(prior)!;
        Assert.True(priorMap.ContainsKey($"{ApprovedKey}|Steam"));
        Assert.False(priorMap.ContainsKey($"{HklmApprovedKey}|Discord"));
    }

    [Fact]
    public void Fix_AllWritesDenied_Throws()
    {
        var (ctx, reg) = Ctx("Steam");
        reg.DenyWriteKeys.Add(ApprovedKey);

        var rule = new StartupBloatRule();
        var ex = Assert.Throws<InvalidOperationException>(() => rule.Fix(ctx));
        Assert.Contains("administrator", ex.Message);
    }
}
