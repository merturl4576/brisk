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

    /// False when schtasks refused — Group Policy, an AV product, a
    /// locked-down task store. Discarding that exit code is what let
    /// settings.json and the Settings checkbox both say "on" for a task that
    /// was never created, with brisk simply not starting with Windows.
    public bool Apply(bool on)
    {
        var ok = on ? CreateTask() : DeleteTask();
        // Either direction is a fresh decision about brisk's autostart, and
        // the old Run value is part of the answer — left behind after "off" it
        // would have brisk claiming to have left startup while still sitting
        // in it. Migrate() does the same for users who never touch the toggle.
        RemoveLegacyValue();
        return ok;
    }

    /// Called once at startup, with the user's CURRENT autostart setting.
    ///
    /// The old Run value is evidence of an old intent only. A user who
    /// upgraded to the task-based build and then explicitly turned autostart
    /// off in Settings has precisely this machine state — value present, no
    /// task — and treating the value as consent there would put brisk back
    /// into startup against the newest thing the user actually said. So the
    /// task is created only when the setting still says yes; SettingsViewModel
    /// writes that flag on every toggle, which makes it the honest record.
    ///
    /// The value itself goes unconditionally: it is a dead autostart either
    /// way (Windows skips it now that brisk requires elevation) and a second
    /// "brisk" row in brisk's own startup list whose toggle changes nothing.
    public void Migrate(bool autostartWanted)
    {
        if (_registry.GetString(LegacyRunKey, LegacyValueName) is null) return;
        if (autostartWanted && !IsOn()) CreateTask();
        RemoveLegacyValue();
    }

    private bool CreateTask() =>
        _runner.Run("schtasks.exe",
            $"/Create /F /TN {TaskName} /SC ONLOGON /RL HIGHEST " +
            $"/TR \"\\\"{_exePath}\\\" --tray\"").ExitCode == 0;

    private bool DeleteTask() =>
        _runner.Run("schtasks.exe", $"/Delete /F /TN {TaskName}").ExitCode == 0;

    private void RemoveLegacyValue() =>
        _registry.DeleteValue(LegacyRunKey, LegacyValueName);
}
