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
/// all: Get-Member lists 32 properties on its output, several of them about
/// uploads and not one of them called that. The closest,
/// `TotalBytesUploaded`, read 0 in the same minute that the month counter
/// read 302 MB. A probe built to the plan's letter
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

    /// The window marker, and the only thing in the code that stands behind
    /// the words "current calendar month" in brisk's copy. Without it that
    /// clause rested on the cmdlet's NAME alone — a sentence about a period
    /// nothing in the read had checked.
    internal const string MonthField = "MonthStartDate";

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

    public PeerUpload? UploadedToPeers() => ParseUploaded(Snapshot());

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
            // bound nothing. The read runs as a task so the bound falls on
            // the process instead.
            //
            // WHAT THE BOUND STILL DOES NOT COVER: the read after a wait
            // that SUCCEEDED. If a grandchild inherited the stdout handle
            // and outlived its parent, the pipe stays open and that read
            // waits with nothing on it. Nothing here has been seen do
            // that, and nothing here has watched for it either; the gap is
            // named rather than covered.
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

    /// The two upload fields, kept apart, or null for output brisk does not
    /// recognise as that shape.
    ///
    /// BOTH UPLOAD FIELDS ARE REQUIRED, and so is the month marker. Reporting
    /// whichever half is present would let a snapshot missing the internet
    /// half report the local half as the whole total — a number wrong in the
    /// direction that reassures, which is the direction this wave refuses to
    /// be wrong in. A shape brisk only half recognises is a counter brisk did
    /// not read.
    ///
    /// A HALF BELOW ZERO is not a count of bytes, so it is not reported as
    /// one either. That refusal used to be made about the sum, which is where
    /// the sum was made; the halves are what this reads now, so it is made
    /// about each of them. Stricter on real quantities — a pair like
    /// (-2, +3) summed to a plausible 1 and passed the old check — but not
    /// stricter outright: two huge halves wrapping their sum negative were
    /// refused HERE by the old check and are admitted now, refused one step
    /// later by the rule's Total < 0 arm. AnUploadFigureBelowZero_IsNotACount
    /// pins both movements.
    internal static PeerUpload? ParseUploaded(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            // WHAT THIS CHECKS AND WHAT IT DOES NOT: that the snapshot
            // carries a month marker at all, not what date it holds. Windows
            // writes that marker as a timestamp — on this machine it decodes
            // to 2026-08-01 00:00:00 local, the first of the current
            // calendar month — and brisk reads the field's presence rather
            // than parsing it, so a stale marker would pass. It is still the
            // difference between quoting a window the read saw and quoting
            // one taken from a cmdlet's name.
            if (!document.RootElement.TryGetProperty(MonthField, out _)) return null;
            var lan = Field(document.RootElement, LanField);
            var internet = Field(document.RootElement, InternetField);
            if (lan is null || internet is null) return null;
            if (lan < 0 || internet < 0) return null;
            return new PeerUpload(lan.Value, internet.Value);
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
