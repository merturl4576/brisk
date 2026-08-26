using System;
using System.IO;
using System.Threading.Tasks;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

public sealed class FixJournalTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-fj-").FullName;
    private FixJournal Journal() => new(Path.Combine(_root, "fix-journal.jsonl"));

    [Fact]
    public void FixThenUndo_ThenNothingUndoable()
    {
        var j = Journal();
        j.RecordFix("power-plan", "{\"guid\":\"abc\"}");
        Assert.Equal("{\"guid\":\"abc\"}", j.LastUndoablePriorState("power-plan"));
        j.RecordUndo("power-plan");
        Assert.Null(j.LastUndoablePriorState("power-plan"));
    }

    [Fact]
    public void SecondFix_IsTheUndoableOne()
    {
        var j = Journal();
        j.RecordFix("r", "one");
        j.RecordFix("r", "two");
        Assert.Equal("two", j.LastUndoablePriorState("r"));
    }

    [Fact]
    public void UnknownRule_HasNothingUndoable()
    {
        Assert.Null(Journal().LastUndoablePriorState("nope"));
    }

    [Fact]
    public void CorruptLine_IsSkipped()
    {
        var path = Path.Combine(_root, "fix-journal.jsonl");
        var j = new FixJournal(path);
        j.RecordFix("r", "first");

        // Append corrupt and blank lines directly
        File.AppendAllText(path, "not-json{{{\n");
        File.AppendAllText(path, "\n");
        File.AppendAllText(path, "   \n");

        j.RecordFix("r", "second");

        // Should return the second entry's prior state without throwing
        Assert.Equal("second", j.LastUndoablePriorState("r"));
    }

    /// THE RACE THE SCAN OPENED. Every fix appends to this file, and since
    /// the read-back shipped, a scan READS it — on a background thread, in
    /// the pass that builds the snapshot. File.AppendAllText holds the file
    /// for writing while it writes, and File.ReadAllLines asks to open it
    /// with FileShare.Read, which a live writer is not: the read is refused
    /// with an IOException out of a call that nobody was catching.
    ///
    /// ONE journal object, because a lock is per-object and that is the shape
    /// the app has — EngineHost hands the same FixJournal to the fix runner
    /// and to the scan. Two objects over one path, which is two processes,
    /// are outside what any lock in this class can reach; EngineHost carries a
    /// catch for that and ScanAsync_TheJournalIsHeldByAnotherProcess pins it.
    ///
    /// The red here is a race, so it is a LIKELIHOOD and not a certainty: run
    /// unlocked it failed on 3 of 3 runs, and one run that got lucky would
    /// have passed. The green is not — with the gate taken, no interleaving
    /// of these two loops can reach a refused read.
    [Fact]
    public async Task AppendingWhileTheScanReads_DoesNotThrow()
    {
        var journal = Journal();
        journal.RecordFix("power-plan", "seed");

        var appends = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++) journal.RecordFix($"rule-{i}", "prior");
        });
        var reads = Task.Run(() =>
        {
            for (var i = 0; i < 300; i++) journal.ListUndoable();
        });

        var error = await Record.ExceptionAsync(() => Task.WhenAll(appends, reads));

        Assert.True(error is null,
            "a read of the journal that landed while a fix was appending to it " +
            "threw, and the read-back rides the scan through exactly this " +
            $"call: {error?.GetBaseException().Message}");
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
