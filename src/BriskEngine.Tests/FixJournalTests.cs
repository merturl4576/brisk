using System;
using System.IO;
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

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
