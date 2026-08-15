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

    private IEnumerable<Entry> ReadAll()
    {
        if (!File.Exists(_path)) yield break;
        foreach (var line in File.ReadAllLines(_path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryDeserializeEntry(line);
            if (entry is not null) yield return entry;
        }
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
