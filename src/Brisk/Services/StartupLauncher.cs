using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

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

    /// Where brisk registered itself before the scheduled task. Left behind,
    /// it is a dead autostart — Windows skips it now that brisk requires
    /// elevation — AND a second "brisk" row in brisk's own startup list,
    /// identical to the task-backed one and toggling nothing real.
    internal const string LegacyRunKey =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string LegacyValueName = "brisk";

    private readonly IProcessRunner _runner;
    private readonly IRegistryProbe _registry;
    private readonly string _exePath;

    public StartupLauncher(IProcessRunner runner, IRegistryProbe registry, string exePath)
    {
        _runner = runner;
        _registry = registry;
        _exePath = exePath;
    }

    public bool IsOn() =>
        _runner.Run("schtasks.exe", $"/Query /TN {TaskName}").ExitCode == 0;

    public void Apply(bool on)
    {
        if (on) CreateTask(); else DeleteTask();
        // Either direction is a fresh decision about brisk's autostart, and
        // the old Run value is part of the answer — left behind after "off" it
        // would have brisk claiming to have left startup while still sitting
        // in it. Migrate() does the same for users who never touch the toggle.
        RemoveLegacyValue();
    }

    /// Called once at startup. A machine carrying the old Run value asked, at
    /// some point, for brisk to start with Windows — so the migration honours
    /// that request through the mechanism that actually works today, and only
    /// then drops the value. The value goes only once the task is really
    /// there: dropping it after a failed schtasks call would silently take
    /// away an autostart the user chose.
    public void Migrate()
    {
        if (_registry.GetString(LegacyRunKey, LegacyValueName) is null) return;
        if (!IsOn()) CreateTask();
        if (IsOn()) RemoveLegacyValue();
    }

    private void CreateTask() =>
        _runner.Run("schtasks.exe",
            $"/Create /F /TN {TaskName} /SC ONLOGON /RL HIGHEST " +
            $"/TR \"\\\"{_exePath}\\\" --tray\"");

    private void DeleteTask() =>
        _runner.Run("schtasks.exe", $"/Delete /F /TN {TaskName}");

    private void RemoveLegacyValue() =>
        _registry.DeleteValue(LegacyRunKey, LegacyValueName);
}
