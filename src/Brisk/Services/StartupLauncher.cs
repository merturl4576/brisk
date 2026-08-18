using BriskEngine.Cleaning;

namespace Brisk.Services;

/// Registers brisk to start at logon. Default is OFF: a tool that criticizes
/// startup bloat earns trust by staying out of startup unless asked — and when
/// it is asked, it lists itself among its own startup findings.
///
/// A Scheduled Task rather than HKCU\Run, because brisk requires elevation and
/// Windows silently refuses to auto-start an elevated app from the Run key.
/// "Run with highest privileges" starts it elevated with no UAC prompt.
public sealed class StartupLauncher
{
    public const string TaskName = "brisk-logon";

    private readonly IProcessRunner _runner;
    private readonly string _exePath;

    public StartupLauncher(IProcessRunner runner, string exePath)
    {
        _runner = runner;
        _exePath = exePath;
    }

    public bool IsOn() =>
        _runner.Run("schtasks.exe", $"/Query /TN {TaskName}").ExitCode == 0;

    public void Apply(bool on)
    {
        if (on)
            _runner.Run("schtasks.exe",
                $"/Create /F /TN {TaskName} /SC ONLOGON /RL HIGHEST " +
                $"/TR \"\\\"{_exePath}\\\" --tray\"");
        else
            _runner.Run("schtasks.exe", $"/Delete /F /TN {TaskName}");
    }
}
