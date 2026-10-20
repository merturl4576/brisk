using System;
using System.Collections.Generic;
using System.IO;
using BriskEngine.Actions.Rules;
using BriskEngine.Actions.Switches;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using BriskEngine.Logging;
using BriskEngine.Models;
using BriskEngine.Safety;

namespace BriskEngine.Actions;

/// The real wiring, in one place, for the CLI now and the GUI later.
public sealed class ActionEngine
{
    public DiagnosticContext Context { get; private init; } = null!;
    public ActionRules Rules { get; private init; } = null!;
    public StartupManager Startup { get; private init; } = null!;
    public ActionPlanBuilder Builder { get; private init; } = null!;
    public ActionRunner Runner { get; private init; } = null!;
    public ReceiptRecorder Receipts { get; private init; } = null!;
    public RunStore Runs { get; private init; } = null!;
    public DeferredCleanQueue Deferred { get; private init; } = null!;
    public CleanRunner Cleaner { get; private init; } = null!;
    public LoadSampler Load { get; private init; } = null!;
    public ConnectionReader Connections { get; private init; } = null!;
    public NetworkUsageReader Usage { get; private init; } = null!;
    public IProcessCloser Closer { get; private init; } = null!;

    private ActionEngine() { }

    public static ActionEngine CreateReal(DiagnosticContext ctx, IProcessRunner runner, Func<bool> isElevated)
    {
        var data = ctx.DataDirectory;
        var log = new ActionLog(Path.Combine(data, "action-log.jsonl"));
        var startup = new StartupManager(ctx.Registry, log, ctx.EventLog, ctx.Files);
        var features = new RealOptionalFeatureProbe();
        var hiberfil = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory)!, "hiberfil.sys");
        var rules = new ActionRules(new ActionProbes(runner, new RealUiEffectsProbe(),
            new RealPowerOverlayProbe(), features, new RealScheduledTaskProbe(runner), startup,
            () => File.Exists(hiberfil)));
        var processes = new RealProcessSnapshotProbe();
        var namer = new RealAppNamer();
        var connections = new ConnectionReader(new RealTcpTableProbe(), new RealDnsCacheProbe(isElevated),
            OwnerTable.Default, processes, namer);
        var cleaner = new CleanRunner(new SafetyValidator(), new WindowsRecycler(), log, runner,
            isElevated, new DeleteLockProbe());
        var deferred = new DeferredCleanQueue(Path.Combine(data, "deferred-clean.json"));
        var runs = new RunStore(Path.Combine(data, "runs"));
        var receipts = new ReceiptRecorder(ctx, startup, processes,
            new ReceiptStore(Path.Combine(data, "receipts")), () => connections.Read().TelemetryCount);
        var journal = new FixJournal(Path.Combine(data, "fix-journal.jsonl"));

        return new ActionEngine
        {
            Context = ctx,
            Rules = rules,
            Startup = startup,
            Builder = new ActionPlanBuilder(ctx, rules, startup, new RealSpaceProbe(runner), features),
            Runner = new ActionRunner(ctx, rules, journal, cleaner, deferred, runs, isElevated, receipts: receipts),
            Receipts = receipts,
            Runs = runs,
            Deferred = deferred,
            Cleaner = cleaner,
            Load = new LoadSampler(processes, namer),
            Connections = connections,
            Usage = new NetworkUsageReader(new RealNetworkUsageProbe(runner), namer),
            Closer = new RealProcessCloser(),
        };
    }

    public ActionPlan BuildPlan(ScanResult scan) => Builder.Build(scan, DateTime.UtcNow);

    /// Cleans the queued caches whose apps have closed since.
    public IReadOnlyList<CleanReport> ProcessDeferred() => Deferred.ProcessDue(DateTime.UtcNow,
        Context.RunningApps,
        t => new Scanner(new[] { t }, Context.RunningApps, new DeleteLockProbe()).Scan().Targets[0],
        s => Cleaner.Clean(s, dryRun: false, permanent: true));
}
