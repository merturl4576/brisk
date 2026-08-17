using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BriskEngine.Models;

namespace BriskEngine.Diagnostics.Rules;

public sealed class DiskForecastRule : AdviseRuleBase
{
    private const string DriveRoot = @"C:\";
    private const string FileName = "disk-history.jsonl";
    private const int MaxDaysToWarn = 60;

    private sealed record Sample(DateTime Ts, long Free);

    public override string Id => "disk-forecast";

    public override DiagnosticFinding? Detect(DiagnosticContext ctx)
    {
        var path = Path.Combine(ctx.DataDirectory, FileName);
        var samples = ReadSamples(path);

        var now = DateTime.UtcNow;
        var currentFree = ctx.Disk.FreeBytes(DriveRoot);
        if (samples.Count == 0 || samples[^1].Ts.Date != now.Date)
        {
            Directory.CreateDirectory(ctx.DataDirectory);
            File.AppendAllText(path, JsonSerializer.Serialize(new { ts = now.ToString("O"), free = currentFree }) + "\n");
            samples.Add(new Sample(now, currentFree));
        }

        if (samples.Count < 3) return null;

        var span = (samples[^1].Ts - samples[0].Ts).TotalDays;
        if (span < 7) return null;

        var slope = LeastSquaresSlope(samples);   // bytes/day
        if (slope >= 0) return null;

        var daysToZero = currentFree / -slope;
        if (daysToZero > MaxDaysToWarn) return null;

        var days = (int)Math.Round(daysToZero);
        return new DiagnosticFinding(
            Id, "rule.disk-forecast.title",
            "Disk is on track to fill up",
            $"Free space has been shrinking; disk full in ~{days} days at the current rate.",
            Severity.Warning, Category, ImpactStars: 3, CanFix: false, FixDescription: null,
            EvidenceKey: $"rule.{Id}.evidence",
            EvidenceArgs: new[]
            {
                days.ToString(System.Globalization.CultureInfo.InvariantCulture),
            });
    }

    private static List<Sample> ReadSamples(string path)
    {
        var samples = new List<Sample>();
        if (!File.Exists(path)) return samples;

        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var ts = DateTime.Parse(
                    doc.RootElement.GetProperty("ts").GetString()!,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind);
                var free = doc.RootElement.GetProperty("free").GetInt64();
                samples.Add(new Sample(ts, free));
            }
            catch (Exception)
            {
                // malformed line — skip
            }
        }
        return samples;
    }

    private static double LeastSquaresSlope(List<Sample> samples)
    {
        var first = samples[0].Ts;
        var xs = samples.Select(s => (s.Ts - first).TotalDays).ToArray();
        var ys = samples.Select(s => (double)s.Free).ToArray();
        var n = xs.Length;

        var sumX = xs.Sum();
        var sumY = ys.Sum();
        var sumXY = 0.0;
        var sumXX = 0.0;
        for (var i = 0; i < n; i++)
        {
            sumXY += xs[i] * ys[i];
            sumXX += xs[i] * xs[i];
        }

        var denom = n * sumXX - sumX * sumX;
        if (denom == 0) return 0;
        return (n * sumXY - sumX * sumY) / denom;
    }
}
