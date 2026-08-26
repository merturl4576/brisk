using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using BriskEngine.Models;

namespace Brisk.Services;

/// What the temperature sensors answered at scan time, recorded into the
/// snapshot so the report card can say "could not read" about the scan it
/// is rendering rather than about some later moment.
public sealed record SensorStatus(bool CpuRead, bool GpuRead, bool? MemoryIntegrityOn);

/// Sensors is required, not an optional trailing parameter. It was optional,
/// and the report card filled the gap with `new SensorStatus(true, true, null)`
/// — so a snapshot that recorded nothing about the sensors rendered
/// "Everything brisk tried to read, answered." on a shareable PNG. There is no
/// default here that adds no claim, so there is no default: a caller that
/// cannot say what the sensors did cannot build a snapshot.
///
/// ReadBack is required for the same reason and rides here for a second one.
/// The read-back's whole claim is that the state it reports and the finding
/// the rule reports come from ONE live read — ReadBack.StateOf asks the rule's
/// own IsOn, "so a switch that is back on is exactly a switch brisk is
/// reporting again, and the two surfaces cannot disagree". That holds only
/// while both are taken in the same pass over the same context; a page that
/// asked the host for a fresh read-back after the scan would be a second
/// channel for one claim, and two channels for one claim is how the sensor
/// notice and the report card came to contradict each other once already.
/// An empty list is a claim too — "brisk has turned nothing off that it can
/// re-read" — so there is no default for it either.
public sealed record ScanSnapshot(
    IReadOnlyList<DiagnosticFinding> Findings,
    ScanResult Cleaner,
    int Health,
    DateTime CompletedUtc,
    SensorStatus Sensors,
    IReadOnlyList<ReadBackResult> ReadBack);

/// The only door between view models and the engine. Everything here is
/// fakeable; nothing in ViewModels/ touches probes, registry or files.
public interface IEngineHost
{
    Task<ScanSnapshot> ScanAsync(IProgress<string>? progress = null,
        CancellationToken ct = default);
    FixOutcome Fix(string ruleId);
    FixOutcome Undo(string ruleId);
    /// Makes the display mode that is on screen right now permanent. The
    /// display fix is applied for the session only (IDisplayProbe), so the
    /// registry is not told until the user has answered "the picture is back"
    /// — a mode nobody can see must never be the mode the machine boots into.
    FixOutcome KeepDisplayFix();
    /// onEntry (additive, round 10): every recorded entry, as it happens,
    /// on the calling thread — the GUI's live cleaning progress.
    CleanReport Clean(TargetScanResult scan, bool dryRun,
        Action<CleanEntry>? onEntry = null);
    IReadOnlyList<UndoableFix> ListUndoable();
    IReadOnlyList<ActionLogEntry> ReadLog(int max = 200);
    IReadOnlyList<StartupEntry> ListStartup();
    bool SetStartupEnabled(string hive, string name, bool enabled);
    bool RunElevated(string cliArgs);
    bool CreateRestorePoint();
    long FreeDiskBytes();
    long LifetimeReclaimedBytes();
    bool IsElevated();
    /// Who brisk is running as versus who is signed in. On a standard account
    /// UAC hands brisk an ADMINISTRATOR's token, and every per-user path —
    /// HKCU, %LOCALAPPDATA%, the Recycle Bin — then follows that token instead
    /// of the person at the keyboard.
    SessionIdentity Session();
}
