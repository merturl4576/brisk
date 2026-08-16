using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class BrowserGpuRuleTests
{
    private const string AppPaths = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    private const string Prefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";
    private const string ChromeExe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeRegistry, FakeSensors) Ctx()
    {
        var ctx = TestContext.Empty();
        var reg = (FakeRegistry)ctx.Registry;
        var sensors = (FakeSensors)ctx.Sensors;
        sensors.Gpus = 2;
        reg.SetString($@"{AppPaths}\chrome.exe", "", ChromeExe);
        return (ctx, reg, sensors);
    }

    [Fact]
    public void HybridGpu_BrowserWithoutPreference_IsAFinding()
    {
        var (ctx, _, _) = Ctx();
        var finding = new BrowserGpuRule().Detect(ctx);
        Assert.NotNull(finding);
        Assert.Contains("chrome", finding!.Evidence, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SingleGpu_NoFinding()
    {
        var (ctx, _, sensors) = Ctx();
        sensors.Gpus = 1;
        Assert.Null(new BrowserGpuRule().Detect(ctx));
    }

    [Fact]
    public void PreferenceAlreadySet_NoFinding()
    {
        var (ctx, reg, _) = Ctx();
        reg.SetString(Prefs, ChromeExe, "GpuPreference=2;");
        Assert.Null(new BrowserGpuRule().Detect(ctx));
    }

    [Fact]
    public void FixThenUndo_RoundTrips()
    {
        var (ctx, reg, _) = Ctx();
        var rule = new BrowserGpuRule();
        var prior = rule.Fix(ctx);
        Assert.Equal("GpuPreference=2;", reg.GetString(Prefs, ChromeExe));
        rule.Undo(ctx, prior);
        Assert.Null(reg.GetString(Prefs, ChromeExe)); // was absent before the fix
    }

    [Fact]
    public void UndoneFix_IsDetectedAgain()
    {
        // The undo round-trip's engine half: after an undo restores the
        // registry, the very next Detect honestly reports the finding again
        // (the GUI then routes it back to the Performans list).
        var (ctx, _, _) = Ctx();
        var rule = new BrowserGpuRule();
        Assert.NotNull(rule.Detect(ctx));
        var prior = rule.Fix(ctx);
        Assert.Null(rule.Detect(ctx));      // fixed: no finding
        rule.Undo(ctx, prior);
        Assert.NotNull(rule.Detect(ctx));   // undone: the finding is back
    }
}
