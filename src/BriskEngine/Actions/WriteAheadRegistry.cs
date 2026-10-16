using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Diagnostics;

namespace BriskEngine.Actions;

/// Records each registry value BEFORE its first write. A rule that throws
/// halfway is rolled back from memory; a process that dies halfway is rolled
/// back from the file at the next start. Rules need no change: the runner
/// hands them this instead of the real registry.
public sealed class WriteAheadRegistry : IRegistryProbe
{
    public sealed record Prior(string KeyPath, string ValueName, string Kind,
        string? Text, int? Number, byte[]? Bytes);

    private readonly IRegistryProbe _inner;
    private readonly string _walPath;
    private readonly List<Prior> _priors = new();
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);

    public WriteAheadRegistry(IRegistryProbe inner, string walPath)
    {
        _inner = inner;
        _walPath = walPath;
        Directory.CreateDirectory(Path.GetDirectoryName(walPath)!);
    }

    public IReadOnlyList<Prior> Priors => _priors;

    private void Remember(string k, string v)
    {
        if (!_seen.Add($"{k}::{v}")) return;
        var prior = Read(_inner, k, v);
        _priors.Add(prior);
        File.AppendAllText(_walPath, JsonSerializer.Serialize(prior) + "\n");
    }

    internal static Prior Read(IRegistryProbe reg, string k, string v)
    {
        var n = reg.GetInt(k, v);
        if (n is not null) return new Prior(k, v, "dword", null, n, null);
        var s = reg.GetString(k, v);
        if (s is not null) return new Prior(k, v, "string", s, null, null);
        var b = reg.GetBytes(k, v);
        if (b is not null) return new Prior(k, v, "binary", null, null, b);
        return new Prior(k, v, "absent", null, null, null);
    }

    private static bool Same(Prior a, Prior b) =>
        a.Kind == b.Kind && a.Number == b.Number && a.Text == b.Text
        && (a.Bytes ?? Array.Empty<byte>()).SequenceEqual(b.Bytes ?? Array.Empty<byte>());

    /// Puts one value back unless it already reads as the prior. Returns
    /// false when the restore itself was refused.
    internal static bool Restore(IRegistryProbe reg, Prior p)
    {
        try
        {
            if (Same(Read(reg, p.KeyPath, p.ValueName), p)) return true;
            switch (p.Kind)
            {
                case "dword": reg.SetInt(p.KeyPath, p.ValueName, p.Number!.Value); break;
                case "string": reg.SetString(p.KeyPath, p.ValueName, p.Text!); break;
                case "binary": reg.SetBytes(p.KeyPath, p.ValueName, p.Bytes!); break;
                default: reg.DeleteValue(p.KeyPath, p.ValueName); break;
            }
            return true;
        }
        catch (Exception) { return false; }
    }

    /// Restores every recorded value, newest first. Returns how many could
    /// not be restored; the record is dropped either way.
    public int RollBack()
    {
        var failed = Enumerable.Reverse(_priors).Count(p => !Restore(_inner, p));
        Commit();
        return failed;
    }

    /// The writes stand, so the record is no longer needed.
    public void Commit()
    {
        _priors.Clear();
        _seen.Clear();
        File.Delete(_walPath);
    }

    /// Restores what a run that died left recorded. Returns how many values.
    public static int RecoverPending(IRegistryProbe registry, string walPath)
    {
        if (!File.Exists(walPath)) return 0;
        var priors = new List<Prior>();
        foreach (var line in File.ReadAllLines(walPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<Prior>(line) is { } p) priors.Add(p);
            }
            catch (JsonException) { }
        }
        foreach (var p in Enumerable.Reverse(priors)) Restore(registry, p);
        File.Delete(walPath);
        return priors.Count;
    }

    public string? GetString(string k, string v) => _inner.GetString(k, v);
    public byte[]? GetBytes(string k, string v) => _inner.GetBytes(k, v);
    public int? GetInt(string k, string v) => _inner.GetInt(k, v);
    public long? GetLong(string k, string v) => _inner.GetLong(k, v);
    public IReadOnlyList<string> GetValueNames(string k) => _inner.GetValueNames(k);
    public IReadOnlyList<string> GetSubKeyNames(string k) => _inner.GetSubKeyNames(k);

    public void SetString(string k, string v, string value) { Remember(k, v); _inner.SetString(k, v, value); }
    public void SetBytes(string k, string v, byte[] value) { Remember(k, v); _inner.SetBytes(k, v, value); }
    public void SetInt(string k, string v, int value) { Remember(k, v); _inner.SetInt(k, v, value); }
    public void DeleteValue(string k, string v) { Remember(k, v); _inner.DeleteValue(k, v); }
}
