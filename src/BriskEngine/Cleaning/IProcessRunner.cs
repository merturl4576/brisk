using System.Diagnostics;

namespace BriskEngine.Cleaning;

public interface IProcessRunner
{
    (int ExitCode, string StdOut) Run(string exe, string args);
}

public sealed class RealProcessRunner : IProcessRunner
{
    /// Standard error is captured and dropped: a child's own error lines
    /// (schtasks asked about a task this build does not have, docker with no
    /// engine running) must not land on brisk's console. The exit code is
    /// what callers act on.
    internal static ProcessStartInfo StartInfo(string exe, string args) => new(exe, args)
    {
        CreateNoWindow = true,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };

    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        using var p = Process.Start(StartInfo(exe, args))!;
        // Drained as it arrives, so a child that writes much to it never
        // blocks on a full pipe while stdout is being read.
        p.ErrorDataReceived += (_, _) => { };
        p.BeginErrorReadLine();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }
}
