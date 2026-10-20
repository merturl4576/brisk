using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using BriskEngine.Cleaning;
using BriskEngine.Diagnostics;

namespace BriskEngine.Live;

public sealed record UsageRow(string App, long BytesSent, long BytesReceived);

public interface INetworkUsageProbe
{
    /// null: the record could not be read.
    IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days);
}

/// The record behind Settings > Network > Data usage, read through
/// PowerShell: calling the WinRT API from .NET directly would need a
/// Windows-versioned target framework and ~25 MB of projection assembly.
public sealed class RealNetworkUsageProbe : INetworkUsageProbe
{
    internal const string Script = """
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
        [void][Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
        $listType = [System.Collections.Generic.IReadOnlyList[Windows.Networking.Connectivity.AttributedNetworkUsage]]
        $end = [DateTimeOffset]::Now
        $start = $end.AddDays(-__DAYS__)
        $states = New-Object Windows.Networking.Connectivity.NetworkUsageStates
        $sum = @{}
        foreach ($p in [Windows.Networking.Connectivity.NetworkInformation]::GetConnectionProfiles()) {
          try {
            $task = $asTask.MakeGenericMethod($listType).Invoke($null, @($p.GetAttributedNetworkUsageAsync($start, $end, $states)))
            if (-not $task.Wait(20000)) { continue }
            foreach ($u in $task.Result) {
              $k = [string]$u.AttributionId
              if (-not $sum.ContainsKey($k)) { $sum[$k] = @([double]0, [double]0) }
              $sum[$k][0] += [double]$u.BytesSent
              $sum[$k][1] += [double]$u.BytesReceived
            }
          } catch { }
        }
        $rows = @($sum.GetEnumerator() | ForEach-Object { [pscustomobject]@{ id = $_.Key; sent = $_.Value[0]; recv = $_.Value[1] } })
        ConvertTo-Json -InputObject $rows -Compress
        """;

    private readonly IProcessRunner _runner;

    public RealNetworkUsageProbe(IProcessRunner runner) { _runner = runner; }

    public IReadOnlyList<(string Id, long Sent, long Received)>? Usage(int days)
    {
        var script = Script.Replace("__DAYS__", days.ToString(CultureInfo.InvariantCulture));
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        try
        {
            var (exit, output) = _runner.Run("powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}");
            return exit == 0 ? Parse(output) : null;
        }
        catch (Exception) { return null; }
    }

    internal static IReadOnlyList<(string Id, long Sent, long Received)>? Parse(string json)
    {
        json = json.Trim();
        if (json.Length == 0) return Array.Empty<(string, long, long)>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var items = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToList()
                : new List<JsonElement> { doc.RootElement };
            return items.Select(e => (
                    Id: e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString()! : "",
                    Sent: (long)e.GetProperty("sent").GetDouble(),
                    Received: (long)e.GetProperty("recv").GetDouble()))
                .ToList();
        }
        catch (Exception) { return null; }
    }
}

public sealed class NetworkUsageReader
{
    public const string WindowsSystem = "Windows (system)";

    private readonly INetworkUsageProbe _probe;
    private readonly IAppNamer _namer;

    public NetworkUsageReader(INetworkUsageProbe probe, IAppNamer namer)
    {
        _probe = probe;
        _namer = namer;
    }

    public IReadOnlyList<UsageRow>? Top(int days = 30, int take = 10)
    {
        var raw = _probe.Usage(days);
        if (raw is null) return null;
        return raw.Select(r => (App: NameOf(r.Id), r.Sent, r.Received))
            .GroupBy(r => r.App, StringComparer.OrdinalIgnoreCase)
            .Select(g => new UsageRow(g.Key, g.Sum(x => x.Sent), g.Sum(x => x.Received)))
            .OrderByDescending(r => r.BytesSent)
            .Take(take)
            .ToList();
    }

    /// An exe's device path, a Store package family name, or empty for
    /// traffic Windows attributes to itself.
    private string NameOf(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return WindowsSystem;
        if (id.Contains('\\')) return _namer.Name(Path.GetFileName(id), id);
        return StartupManager.FriendlyPackageName(id);
    }
}
