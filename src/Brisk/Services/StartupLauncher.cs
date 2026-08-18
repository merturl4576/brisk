using System;
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

    /// The Run value never travels alone: Explorer keeps its enabled/disabled
    /// bit in a parallel StartupApproved record, written by Task Manager and
    /// read by brisk's own startup list. Dropping only the value leaves that
    /// record behind as a permanent description of an entry that no longer
    /// exists — and it would silently re-decide the toggle for any future
    /// "brisk" Run value that ever appeared here.
    internal const string LegacyApprovedKey =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly IProcessRunner _runner;
    private readonly IRegistryProbe _registry;
    private readonly string _exePath;
    /// Null until first asked. The task state is read by two surfaces (the
    /// Settings checkbox and the Startup page's brisk row) and by the
    /// migration, and every read used to be a schtasks.exe launch — including
    /// one on the dispatcher, from a binding evaluated in MainWindow's
    /// constructor. One spawn, then the cache, invalidated by the only thing
    /// that can change the answer: Apply.
    private bool? _isOn;

    public StartupLauncher(IProcessRunner runner, IRegistryProbe registry, string exePath)
    {
        _runner = runner;
        _registry = registry;
        _exePath = exePath;
    }

    public bool IsOn() => _isOn ??=
        _runner.Run("schtasks.exe", $"/Query /TN {TaskName}").ExitCode == 0;

    /// Raised after Apply changes (or fails to change) the task. Both surfaces
    /// that show brisk's own autostart subscribe: with one backing truth but
    /// no notification, WPF kept the value it read when the page was built, so
    /// turning brisk off on the Startup page still left the Settings checkbox
    /// showing "on" for the life of the process.
    public event Action? Changed;

    /// False when schtasks refused — Group Policy, an AV product, a
    /// locked-down task store. Discarding that exit code is what let
    /// settings.json and the Settings checkbox both say "on" for a task that
    /// was never created, with brisk simply not starting with Windows.
    public bool Apply(bool on)
    {
        var ok = on ? CreateTask() : DeleteTask();
        // A refused apply leaves the task as it was, but "as it was" is no
        // longer something this class may assume — re-ask on the next read.
        _isOn = ok ? on : null;
        // Either direction is a fresh decision about brisk's autostart, and
        // the old Run value is part of the answer — left behind after "off" it
        // would have brisk claiming to have left startup while still sitting
        // in it. Migrate() does the same for users who never touch the toggle.
        RemoveLegacyValue();
        Changed?.Invoke();
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
        if (autostartWanted && !IsOn()) { CreateTask(); _isOn = null; }
        RemoveLegacyValue();
    }

    private bool CreateTask() =>
        _runner.Run("schtasks.exe",
            $"/Create /F /TN {TaskName} /SC ONLOGON /RL HIGHEST " +
            $"/TR \"\\\"{_exePath}\\\" --tray\"").ExitCode == 0;

    private bool DeleteTask() =>
        _runner.Run("schtasks.exe", $"/Delete /F /TN {TaskName}").ExitCode == 0;

    private void RemoveLegacyValue()
    {
        _registry.DeleteValue(LegacyRunKey, LegacyValueName);
        _registry.DeleteValue(LegacyApprovedKey, LegacyValueName);
    }
}
