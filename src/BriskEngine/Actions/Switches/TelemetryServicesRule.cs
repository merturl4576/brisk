using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions.Switches;

/// The DiagTrack switch, which also stops the services now: the start type
/// alone waits for a reboot, and the receipt counts telemetry connections two
/// minutes after the run.
public sealed class TelemetryServicesRule : SwitchRule
{
    private readonly IProcessRunner _runner;

    public TelemetryServicesRule(IProcessRunner runner) : base(SwitchCatalog.TelemetryServices)
    {
        _runner = runner;
    }

    public override string Fix(DiagnosticContext ctx)
    {
        var prior = base.Fix(ctx);
        _runner.Run("sc.exe", "stop DiagTrack");
        _runner.Run("sc.exe", "stop dmwappushservice");
        return prior;
    }

    public override void Undo(DiagnosticContext ctx, string priorStateJson)
    {
        base.Undo(ctx, priorStateJson);
        if (ctx.Registry.GetInt($@"{SwitchCatalog.Services}\DiagTrack", "Start") == 2)
            _runner.Run("sc.exe", "start DiagTrack");
    }
}
