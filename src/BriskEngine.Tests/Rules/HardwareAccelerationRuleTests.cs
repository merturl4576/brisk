using BriskEngine.Diagnostics.Rules;
using BriskEngine.Paths;
using Xunit;

namespace BriskEngine.Tests.Rules;

public class HardwareAccelerationRuleTests
{
    private static readonly string ChromeLocalState =
        PathExpander.Expand(@"%LOCALAPPDATA%\Google\Chrome\User Data\Local State")!;

    private static (BriskEngine.Diagnostics.DiagnosticContext, FakeFiles, FakeRunningApps) Ctx(string json)
    {
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        var apps = (FakeRunningApps)ctx.RunningApps;
        files.Texts[ChromeLocalState] = json;
        return (ctx, files, apps);
    }

    [Fact]
    public void DisabledAcceleration_IsAFinding()
    {
        var (ctx, _, _) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        Assert.NotNull(new HardwareAccelerationRule().Detect(ctx));
    }

    [Fact]
    public void EnabledOrAbsent_NoFinding()
    {
        var (ctx, _, _) = Ctx("""{"browser":{}}""");
        Assert.Null(new HardwareAccelerationRule().Detect(ctx));
    }

    [Fact]
    public void Fix_WhileBrowserRunning_Throws()
    {
        var (ctx, _, apps) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        apps.Running.Add("chrome");
        Assert.Throws<System.InvalidOperationException>(() => new HardwareAccelerationRule().Fix(ctx));
    }

    [Fact]
    public void FixThenUndo_RoundTrips()
    {
        var (ctx, files, _) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        var rule = new HardwareAccelerationRule();
        var prior = rule.Fix(ctx);
        Assert.Contains("\"enabled\":true", files.Texts[ChromeLocalState].Replace(" ", ""));
        rule.Undo(ctx, prior);
        Assert.Contains("\"enabled\":false", files.Texts[ChromeLocalState].Replace(" ", ""));
    }
}
