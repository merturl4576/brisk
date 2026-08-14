using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BriskEngine.Diagnostics;

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

    private void Append(Entry entry)
    {
        lock (_gate) File.AppendAllText(_path, JsonSerializer.Serialize(entry) + "\n");
    }

    private IEnumerable<Entry> ReadAll()
    {
        if (!File.Exists(_path)) yield break;
        foreach (var line in File.ReadAllLines(_path))
            if (JsonSerializer.Deserialize<Entry>(line) is { } e) yield return e;
    }
}
