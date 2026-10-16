using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BriskEngine.Actions;

public sealed class RunStore
{
    private readonly string _dir;

    public RunStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(directory);
    }

    public void Save(RunRecord run) =>
        File.WriteAllText(Path.Combine(_dir, $"{run.RunId}.json"), JsonSerializer.Serialize(run));

    public RunRecord? Load(string runId)
    {
        var path = Path.Combine(_dir, $"{runId}.json");
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path)); }
        catch (JsonException) { return null; }
    }

    /// Run ids start with a UTC timestamp, so the newest sorts last.
    public RunRecord? Latest() => Directory.EnumerateFiles(_dir, "*.json")
        .OrderByDescending(f => f, StringComparer.Ordinal)
        .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
        .FirstOrDefault(r => r is not null);
}
