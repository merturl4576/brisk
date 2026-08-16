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
            processInfo, sensors,
            new RealDiskInfoProbe(), new RealFileProbe(),
            new RealProcessLister(), dataDir);
        var logPath = Path.Combine(dataDir, "action-log.jsonl");
        var log = new ActionLog(logPath);
        var journal = new FixJournal(Path.Combine(dataDir, "fix-journal.jsonl"));
        var host = new EngineHost(ctx, DiagnosticRuleRegistry.All,
            new Scanner(CleanupTargetRegistry.All, new RealProcessLister()),
            new FixRunner(journal, log),
            new CleanRunner(new SafetyValidator(), new WindowsRecycler(), log, runner,
                () => new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)),
            journal, new StartupManager(registry, log), logPath,
            Path.Combine(AppContext.BaseDirectory, "Brisk.Cli.exe"));

        var settingsPath = Path.Combine(dataDir, "settings.json");
        return new AppComposition
        {
            Host = host,
            LiveMetrics = new LiveMetrics(sensors, processInfo, host.FreeDiskBytes),
            Settings = Settings.Load(settingsPath),
            SettingsPath = settingsPath,
            Launcher = new StartupLauncher(registry,
                Path.Combine(AppContext.BaseDirectory, "brisk-app.exe")),
        };
    }
}
