using System;
using System.IO;
using System.Linq;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public sealed class JournalQueryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-jq-").FullName;

    [Fact]
    public void ListUndoable_TracksFixThenUndo()
    {
        var journal = new FixJournal(Path.Combine(_root, "j.jsonl"));
        journal.RecordFix("power-plan", "{}");
        journal.RecordFix("visual-effects", "{}");
        journal.RecordUndo("power-plan");

        var undoable = journal.ListUndoable();
        Assert.Single(undoable);
        Assert.Equal("visual-effects", undoable[0].RuleId);
    }

    [Fact]
    public void ListUndoable_RefixAfterUndo_IsUndoableAgain()
    {
        var journal = new FixJournal(Path.Combine(_root, "j2.jsonl"));
        journal.RecordFix("power-plan", "{}");
        journal.RecordUndo("power-plan");
        journal.RecordFix("power-plan", "{}");
        Assert.Equal("power-plan", Assert.Single(journal.ListUndoable()).RuleId);
    }

    [Fact]
    public void ListUndoable_EmptyJournal_IsEmpty() =>
        Assert.Empty(new FixJournal(Path.Combine(_root, "j3.jsonl")).ListUndoable());

    [Fact]
    public void ReadTail_ParsesFixAndCleanLines_NewestFirst()
    {
        var path = Path.Combine(_root, "log.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"ts":"2026-08-15T10:00:00Z","ruleId":"power-plan","action":"fix"}""",
            "not json at all",
            """{"ts":"2026-08-15T11:00:00Z","targetId":"user-temp","path":"C:\\t\\x.tmp","bytes":2048,"action":"recycled","reason":null}""",
        });

        var entries = ActionLogReader.ReadTail(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal("clean", entries[0].Kind);
        Assert.Contains("user-temp", entries[0].Summary);
        Assert.Contains("2 KB", entries[0].Summary);
        Assert.Equal("fix", entries[1].Kind);
        Assert.Contains("power-plan", entries[1].Summary);
    }

    [Fact]
    public void ReadTail_MissingFile_IsEmpty() =>
        Assert.Empty(ActionLogReader.ReadTail(Path.Combine(_root, "nope.jsonl")));

    [Fact]
    public void TotalRecycledBytes_SumsOnlyRecycledLines()
    {
        var path = Path.Combine(_root, "life.jsonl");
        File.WriteAllLines(path, new[]
        {
            """{"ts":"2026-08-15T10:00:00Z","targetId":"user-temp","path":"C:\\a","bytes":100,"action":"recycled","reason":null}""",
            """{"ts":"2026-08-15T10:01:00Z","targetId":"user-temp","path":"C:\\b","bytes":900,"action":"dry-run","reason":null}""",
            """{"ts":"2026-08-15T10:02:00Z","targetId":"npm-cache","path":"C:\\c","bytes":50,"action":"recycled","reason":null}""",
            """{"ts":"2026-08-15T10:03:00Z","ruleId":"power-plan","action":"fix"}""",
        });
        Assert.Equal(150, ActionLogReader.TotalRecycledBytes(path));
        Assert.Equal(0, ActionLogReader.TotalRecycledBytes(Path.Combine(_root, "no.jsonl")));
    }

    [Fact]
    public void ReadTail_RespectsMax()
    {
        var path = Path.Combine(_root, "big.jsonl");
        File.WriteAllLines(path, Enumerable.Range(0, 50).Select(i =>
            "{\"ts\":\"2026-08-15T10:00:" + i.ToString("00") + "Z\",\"ruleId\":\"r" + i + "\",\"action\":\"fix\"}"));
        Assert.Equal(10, ActionLogReader.ReadTail(path, 10).Count);
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
