using System;
using System.IO;
using System.Text.Json;
using BriskEngine.Logging;
using Xunit;

namespace BriskEngine.Tests;

public sealed class ActionLogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-log-").FullName;

    [Fact]
    public void AppendsOneJsonObjectPerLine()
    {
        var path = Path.Combine(_root, "sub", "log.jsonl");
        var log = new ActionLog(path);
        log.Append(new { action = "recycled", bytes = 42 });
        log.Append(new { action = "refused", bytes = 0 });
        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Equal("recycled",
            JsonDocument.Parse(lines[0]).RootElement.GetProperty("action").GetString());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
