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

    [Fact]
    public void Fix_MixedOffenders_OneRunning_WritesNothing()
    {
        var edgeLocalState = PathExpander.Expand(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State")!;
        var ctx = TestContext.Empty();
        var files = (FakeFiles)ctx.Files;
        var apps = (FakeRunningApps)ctx.RunningApps;

        // Both Chrome and Edge have disabled acceleration
        files.Texts[ChromeLocalState] = """{"hardware_acceleration_mode":{"enabled":false}}""";
        files.Texts[edgeLocalState] = """{"hardware_acceleration_mode":{"enabled":false}}""";

        // Only Edge is running
        apps.Running.Add("msedge");

        // Fix should throw before writing anything
        Assert.Throws<System.InvalidOperationException>(() => new HardwareAccelerationRule().Fix(ctx));

        // Verify Chrome file was NOT modified (still contains "enabled":false)
        Assert.Contains("\"enabled\":false", files.Texts[ChromeLocalState].Replace(" ", ""));
    }
}
