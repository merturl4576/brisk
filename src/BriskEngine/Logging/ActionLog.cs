using System.IO;
using System.Text.Json;

namespace BriskEngine.Logging;

public sealed class ActionLog
{
    private readonly string _path;
    private readonly object _gate = new();

    public ActionLog(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Append(object entry)
    {
        var line = JsonSerializer.Serialize(entry);
        lock (_gate) File.AppendAllText(_path, line + "\n");
    }
}
