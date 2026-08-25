using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Logging;
using BriskEngine.Safety;

namespace Brisk.Services;

public sealed class AppComposition
{
    public required IEngineHost Host { get; init; }
    public required ILiveMetrics LiveMetrics { get; init; }
    public required Settings Settings { get; init; }
    public required string SettingsPath { get; init; }
    public required StartupLauncher Launcher { get; init; }
}

public static class AppServices
{
    public static AppComposition Build()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "brisk");
        var runner = new RealProcessRunner();
        var registry = new RealRegistryProbe();
        // Shared with LiveMetrics below — one LibreHardwareMonitor session and
        // one memory-status reader for both diagnostics and the live tiles.
        var processInfo = new RealProcessInfoProbe();
        // RealSensorProbe is IDisposable but lives for the whole app lifetime.
        var sensors = new RealSensorProbe();
        var ctx = new DiagnosticContext(
            new RealPowercfgProbe(runner), registry,
            processInfo, sensors, new RealDisplayProbe(),
            new RealEventLogProbe(), new RealHardwareProbe(),
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), new RealMemoryIntegrityProbe(),
            new RealDeliveryOptimizationProbe(), dataDir);
        var logPath = Path.Combine(dataDir, "action-log.jsonl");
        var log = new ActionLog(logPath);
        var journal = new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl"));
        var host = new EngineHost(ctx, DiagnosticRuleRegistry.All,
            new Scanner(CleanupTargetRegistry.All, new RealProcessLister(),
                new DeleteLockProbe()),
            new FixRunner(journal, log),
            new CleanRunner(new SafetyValidator(), new WindowsRecycler(), log, runner,
                () => new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator),
                new DeleteLockProbe()),
            journal, new StartupManager(registry, log), logPath,
            SelfPath, new RealSessionProbe());

        var settingsPath = Path.Combine(dataDir, "settings.json");
        return new AppComposition
        {
            Host = host,
            LiveMetrics = new LiveMetrics(sensors, processInfo, host.FreeDiskBytes),
            Settings = Settings.Load(settingsPath),
            SettingsPath = settingsPath,
            Launcher = new StartupLauncher(runner, registry, SelfPath),
        };
    }

    /// Both the elevated re-launch and the autostart task used to be spelled
    /// as a file name next to the app: "Brisk.Cli.exe" for the first,
    /// "brisk-app.exe" for the second. Neither name exists beside a
    /// single-file build — the console tool is inside this executable now, and
    /// the executable is whatever the user renamed their download to. So both
    /// point at the running file itself, which is true in every build.
    private static string SelfPath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "brisk-app.exe");
}
