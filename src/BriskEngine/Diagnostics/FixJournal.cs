using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BriskEngine.Diagnostics;

public sealed record UndoableFix(string RuleId, System.DateTime FixedAtUtc);

public sealed class FixJournal
{
    private sealed record Entry(string RuleId, string Action, string? PriorState, System.DateTime Ts);

    private readonly string _path;
    private readonly object _gate = new();

    public FixJournal(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void RecordFix(string ruleId, string priorStateJson) =>
        Append(new Entry(ruleId, "fix", priorStateJson, System.DateTime.UtcNow));

    public void RecordUndo(string ruleId) =>
        Append(new Entry(ruleId, "undo", null, System.DateTime.UtcNow));

    public string? LastUndoablePriorState(string ruleId)
    {
        string? candidate = null;
        foreach (var entry in ReadAll())
        {
            if (entry.RuleId != ruleId) continue;
            candidate = entry.Action == "fix" ? entry.PriorState : null;
        }
        return candidate;
    }

    public IReadOnlyList<UndoableFix> ListUndoable()
    {
        var last = new Dictionary<string, System.DateTime>();
        foreach (var entry in ReadAll())
        {
            if (entry.Action == "fix" && entry.PriorState is not null)
                last[entry.RuleId] = entry.Ts;
            else if (entry.Action == "undo")
                last.Remove(entry.RuleId);
        }
        return last.Select(kv => new UndoableFix(kv.Key, kv.Value))
            .OrderByDescending(u => u.FixedAtUtc).ToList();
    }

    private void Append(Entry entry)
    {
        lock (_gate) File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n");
    }

    /// Under the SAME gate as Append. Since the read-back shipped, a scan
    /// reads this file on a background thread while a fix can be appending to
    /// it, and File.ReadAllLines asks to open the file with FileShare.Read —
    /// which a live append does not grant, so the read is refused outright
    /// with an IOException.
    ///
    /// The gate is per-object, so what it serialises is the one journal
    /// EngineHost hands to both the fix runner and the scan. A second process
    /// holding the same path is outside anything a lock here can reach;
    /// EngineHost carries a catch for that.
    ///
    /// The lines are read under the lock and parsed outside it. This was an
    /// iterator, and a lazy read cannot hold a lock across its caller's loop.
    private IEnumerable<Entry> ReadAll()
    {
        string[] lines;
        lock (_gate)
        {
            if (!File.Exists(_path)) return System.Array.Empty<Entry>();
            lines = File.ReadAllLines(_path);
        }
        var entries = new List<Entry>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryDeserializeEntry(line);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    private Entry? TryDeserializeEntry(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<Entry>(line);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
