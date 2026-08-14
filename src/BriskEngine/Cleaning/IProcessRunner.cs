using System.Diagnostics;

namespace BriskEngine.Cleaning;

public interface IProcessRunner
{
    (int ExitCode, string StdOut) Run(string exe, string args);
}

public sealed class RealProcessRunner : IProcessRunner
{
    public (int ExitCode, string StdOut) Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        })!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout);
    }
}
