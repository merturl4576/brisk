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

    /// A Local State that exists and cannot be parsed used to be swallowed
    /// (catch {}) and reported as nothing — the same silence as a browser
    /// with acceleration on. An unreadable file is a disclosure: brisk says
    /// it could not read it, as a free notice, and offers no fix over it.
    [Fact]
    public void UnreadableLocalState_IsANoticeThatSaysSo()
    {
        var (ctx, _, _) = Ctx("{ not json at all");
        var finding = new HardwareAccelerationRule().Detect(ctx);

        Assert.NotNull(finding);
        Assert.Equal(BriskEngine.Models.FindingKind.Notice, finding!.Kind);
        Assert.Equal(BriskEngine.Models.ImpactClass.Hygiene, finding.ImpactClass);
        Assert.Equal(BriskEngine.Models.Severity.Info, finding.Severity);
        Assert.False(finding.CanFix);
        Assert.Equal("rule.hw-acceleration.unread.title", finding.TitleKey);
        Assert.Equal("rule.hw-acceleration.evidence.unread", finding.EvidenceKey);
        Assert.Equal(new[] { "chrome" }, finding.EvidenceArgs);
        Assert.Contains("could not read", finding.Evidence);
        Assert.Equal(100, BriskEngine.Diagnostics.HealthScore.Compute(new[] { finding }));
    }

    /// One browser readable and off, the other unreadable: the finding is
    /// still the fixable problem, its evidence names both facts, and the fix
    /// writes only the file it could read.
    [Fact]
    public void Offender_BesideAnUnreadableOne_NamesBoth_AndFixesOnlyWhatItRead()
    {
        var edgeLocalState = PathExpander.Expand(@"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State")!;
        var (ctx, files, _) = Ctx("""{"hardware_acceleration_mode":{"enabled":false}}""");
        files.Texts[edgeLocalState] = "{ not json at all";
        var rule = new HardwareAccelerationRule();

        var finding = rule.Detect(ctx);

        Assert.NotNull(finding);
        Assert.True(finding!.CanFix);
        Assert.Equal(BriskEngine.Models.FindingKind.Problem, finding.Kind);
        Assert.Equal("rule.hw-acceleration.evidence.partly", finding.EvidenceKey);
        Assert.Equal(new[] { "chrome", "msedge" }, finding.EvidenceArgs);
        Assert.Contains("chrome", finding.Evidence);
        Assert.Contains("could not read", finding.Evidence);

        rule.Fix(ctx);

        Assert.Contains("\"enabled\":true", files.Texts[ChromeLocalState].Replace(" ", ""));
        Assert.Equal("{ not json at all", files.Texts[edgeLocalState]);
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
