using System;
using System.Diagnostics;
using System.Text.Json;

namespace BriskEngine.Diagnostics.RealProbes;

/// Windows' own Delivery Optimization counter, read through the PowerShell
/// cmdlet that exposes it. No registry value and no Win32 call was found
/// that carries this figure, which is why the probe starts a process to ask
/// — the second probe in brisk that does, beside RealPowercfgProbe, which
/// runs powercfg through the shared process runner.
///
/// THE CMDLET IS NOT THE ONE THE PLAN NAMED, and the reason is a measurement
/// rather than a preference. The plan said
/// `Get-DeliveryOptimizationPerfSnap` and `BytesToPeers`. Run on the machine
/// this was written on, that cmdlet answers with no field of that name at
/// all — its upload figures are `TotalBytesUploaded`, `FilesUploaded` and
/// `AverageUploadSize`, and `TotalBytesUploaded` read 0 in the same minute
/// that the month counter read 302 MB. A probe built to the plan's letter
/// would have reported "unreadable" forever on a machine whose number was
/// sitting there. `Get-DeliveryOptimizationPerfSnapThisMonth` is the cmdlet
/// that carries it, and its name is also where the word "month" in brisk's
/// copy comes from — the period is the cmdlet's claim, not brisk's guess.
///
/// Only this machine has been observed. Whether an older Windows answers
/// with the plan's field names instead is not something one machine can
/// establish, and nothing here guesses at it: an answer brisk does not
/// recognise is an unread counter, never a zero.
public sealed class RealDeliveryOptimizationProbe : IDeliveryOptimizationProbe
{
    /// The two fields that together are "uploaded to other machines": peers
    /// reached over the local network, and peers reached over the internet.
    internal const string LanField = "UploadLanBytes";
    internal const string InternetField = "UploadInternetBytes";

    /// try/catch/exit 1 is the shape this repo already uses to run PowerShell
    /// (EngineHost.CreateRestorePoint), and it is here for a second reason:
    /// stderr is NOT redirected below, so an error PowerShell printed itself
    /// would land in the middle of `brisk scan`'s output. Swallowed in the
    /// shell, the failure reaches brisk as an exit code and nothing else.
    /// -ErrorAction Stop is what makes a non-terminating cmdlet error reach
    /// that catch instead of printing and carrying on.
    internal const string Arguments =
        "-NoProfile -NonInteractive -Command \"try { " +
        "Get-DeliveryOptimizationPerfSnapThisMonth -ErrorAction Stop | " +
        "ConvertTo-Json -Compress } catch { exit 1 }\"";

    /// Measured on this machine: three runs of that command took 971 ms,
    /// 437 ms and 949 ms. Ten seconds is an order of magnitude of headroom,
    /// and a bound exists at all because the alternative is a scan that never
    /// finishes.
    ///
    /// WHY NOT IProcessRunner, which is how the rest of brisk starts a
    /// process: it takes no timeout, and its one implementation blocks in
    /// StandardOutput.ReadToEnd() until the child closes the pipe. Giving it
    /// one would also bind CleanRunner's docker prune, a command brisk has
    /// never timed, and rewriting how the cleaner launches processes is not
    /// this probe's business. So the launch below copies that runner's
    /// ProcessStartInfo exactly and puts the bound here instead.
    private const int TimeoutMs = 10_000;

    public long? BytesUploadedToPeers() => ParseUploadedBytes(Snapshot());

    /// Whatever the cmdlet printed, or null if brisk never got that far. A
    /// failure anywhere in this method becomes that null rather than an
    /// exception: it runs inside a scan, and EngineHost's catch-all would
    /// swallow an exception along with the whole finding rather than
    /// reporting the gap.
    private static string? Snapshot()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(
                "powershell.exe", Arguments)
            {
                // The three the repo's own process runner sets, for the same
                // reasons: no console window on a GUI scan, no shell, and the
                // output captured rather than printed.
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            });
            if (process is null) return null;

            // Started before the wait. StandardOutput.ReadToEnd() blocks
            // until the child closes the pipe, so reading first and waiting
            // second is how the repo's runner does it — but that read is
            // itself unbounded, and a timeout that only bounds the wait would
            // bound nothing. The read runs as a task so the bound is on the
            // process, which is the thing that can hang.
            var stdout = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(TimeoutMs))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception) { /* already gone, or not ours to kill */ }
                return null;
            }
            return process.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// The sum of the two upload fields, or null for output brisk does not
    /// recognise as that shape.
    ///
    /// BOTH FIELDS ARE REQUIRED. Summing whichever one is present would let a
    /// snapshot missing the internet half report the local half as the whole
    /// total — a number that is wrong in the direction that reassures, which
    /// is the direction this wave refuses to be wrong in. A shape brisk only
    /// half recognises is a counter brisk did not read.
    ///
    /// A total below zero is not a count of bytes, so it is not reported as
    /// one either.
    internal static long? ParseUploadedBytes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var lan = Field(document.RootElement, LanField);
            var internet = Field(document.RootElement, InternetField);
            if (lan is null || internet is null) return null;
            var total = lan.Value + internet.Value;
            return total < 0 ? null : total;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// ValueKind is checked before TryGetInt64 because TryGetInt64 does not
    /// answer false for a property of the wrong kind — it THROWS
    /// InvalidOperationException, which is not a JsonException and so walks
    /// straight past the catch above. Found by the test that plants a
    /// quoted figure where a number belongs.
    private static long? Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt64(out var number)
            ? number
            : null;
}
