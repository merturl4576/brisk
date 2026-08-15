using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BriskEngine.Logging;

public sealed record ActionLogEntry(DateTime TsUtc, string Kind, string Summary, string Raw);

public static class ActionLogReader
{
    /// Newest first. Malformed lines are skipped; a missing file is an empty log.
    public static IReadOnlyList<ActionLogEntry> ReadTail(string path, int max = 200)
    {
        if (!File.Exists(path)) return Array.Empty<ActionLogEntry>();
        var entries = new List<ActionLogEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = TryParse(line);
            if (entry is not null) entries.Add(entry);
        }
        entries.Reverse();
        return entries.Take(max).ToList();
    }

    /// Lifetime reclaimed total — every "recycled" clean line ever logged.
    public static long TotalRecycledBytes(string path)
    {
        if (!File.Exists(path)) return 0;
        long total = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("action", out var a)
                    && a.GetString() == "recycled"
                    && root.TryGetProperty("bytes", out var b)
                    && b.ValueKind == JsonValueKind.Number)
                    total += b.GetInt64();
            }
            catch (JsonException) { }
        }
        return total;
    }

    private static ActionLogEntry? TryParse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var ts = root.TryGetProperty("ts", out var tsEl) && tsEl.TryGetDateTime(out var t)
                ? t.ToUniversalTime() : DateTime.MinValue;
            var action = root.TryGetProperty("action", out var a) ? a.GetString() ?? "?" : "?";

            if (root.TryGetProperty("ruleId", out var rule))
                return new ActionLogEntry(ts, "fix", $"{action}: {rule.GetString()}", line);

            if (root.TryGetProperty("targetId", out var target))
            {
                var itemPath = root.TryGetProperty("path", out var p) ? p.GetString() : null;
                var bytes = root.TryGetProperty("bytes", out var b)
                    && b.ValueKind == JsonValueKind.Number ? b.GetInt64() : 0;
                var reason = root.TryGetProperty("reason", out var r)
                    && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
                var summary = $"{action}: {target.GetString()} {itemPath} ({Fmt.Bytes(bytes)})";
                if (reason is not null) summary += $" — {reason}";
                return new ActionLogEntry(ts, "clean", summary, line);
            }

            return new ActionLogEntry(ts, "other", action, line);
        }
        catch (JsonException) { return null; }
        catch (FormatException) { return null; }
    }
}
