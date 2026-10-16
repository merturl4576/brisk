using System;
using System.Management;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace BriskEngine.Actions.Rules;

public interface IOptionalFeatureProbe
{
    /// 1 enabled, 2 disabled, 3 absent; null when it could not be read.
    int? InstallState(string featureName);
}

public sealed class RealOptionalFeatureProbe : IOptionalFeatureProbe
{
    public int? InstallState(string featureName)
    {
        try
        {
            var name = featureName.Replace("'", "");
            using var searcher = new ManagementObjectSearcher(
                $"SELECT InstallState FROM Win32_OptionalFeature WHERE Name = '{name}'");
            foreach (ManagementObject o in searcher.Get())
                using (o) return Convert.ToInt32(o["InstallState"]);
            return 3;
        }
        catch (Exception) { return null; }
    }
}

/// Listed because Microsoft's gaming guidance names it; never ticked by
/// default because WSL and Docker need it.
public sealed class VirtualMachinePlatformRule : IReadBackable
{
    public const string Feature = "VirtualMachinePlatform";
    private readonly IOptionalFeatureProbe _features;
    private readonly IProcessRunner _runner;

    public VirtualMachinePlatformRule(IOptionalFeatureProbe features, IProcessRunner runner)
    {
        _features = features;
        _runner = runner;
    }

    public string Id => "vm-platform";
    public RuleCategory Category => RuleCategory.Confirm;

    public bool IsOn(DiagnosticContext ctx) => _features.InstallState(Feature) == 1;

    public WriteEffect EffectOfTheWrite(DiagnosticContext ctx) => WriteEffect.NotAPolicy;

    public DiagnosticFinding? Detect(DiagnosticContext ctx) => IsOn(ctx)
        ? new DiagnosticFinding(Id, $"rule.{Id}.title", "Virtual Machine Platform is on",
            "The virtualization layer WSL and Docker use is enabled.",
            Severity.Info, Category, ImpactStars: 1, CanFix: true,
            FixDescription: "Turn Virtual Machine Platform off (undoable, needs a restart)", Kind: FindingKind.Notice)
        : null;

    public string Fix(DiagnosticContext ctx)
    {
        Dism($"/Online /Disable-Feature /FeatureName:{Feature} /NoRestart");
        return "\"enabled\"";
    }

    public void Undo(DiagnosticContext ctx, string priorStateJson) =>
        Dism($"/Online /Enable-Feature /FeatureName:{Feature} /All /NoRestart");

    /// 3010: done, restart required — the row is pending restart either way.
    private void Dism(string args)
    {
        var (exit, _) = _runner.Run("dism.exe", args);
        if (exit is not (0 or 3010))
            throw new InvalidOperationException($"DISM exited {exit}");
    }
}
