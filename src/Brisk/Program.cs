using System;
using System.IO;
using System.Runtime.InteropServices;
using Brisk.Cli;

namespace Brisk;

/// brisk used to ship as two executables that had to sit in the same folder:
/// the window, and the console tool the window launched for work that needed
/// its own elevated process. A single-file build has no folder to sit in, so
/// the pair became one executable with two faces — decided here, by
/// EntryRouter, on the way in.
///
/// The window is the default because that is what a double-click means.
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (EntryRouter.RoutesToConsole(args))
        {
            ParentConsole.Adopt();
            // The report verb needs WPF, which only this executable carries,
            // and Brisk.Cli cannot reference this project — so it is answered
            // here, before the console entry point sees the arguments.
            if (args.Length > 0 && args[0] == "report")
                return Services.ReportRunner.Run(args);
            return global::Brisk.Cli.Program.Main(args);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}

/// A GUI-subsystem process gets no console of its own, so without this every
/// line brisk's console mode writes would go nowhere — the command would look
/// like it did nothing at all.
internal static class ParentConsole
{
    private const int AttachParentProcess = -1;
    private const int StdOutputHandle = -11;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    public static void Adopt()
    {
        // Redirection ("brisk scan --json > report.json") hands the process a
        // real handle before it starts. Attaching a console over that would
        // replace the file the caller asked for with a terminal.
        var existing = GetStdHandle(StdOutputHandle);
        if (existing != IntPtr.Zero && existing != InvalidHandle) return;

        // Fails when nothing launched brisk from a terminal — the elevated
        // re-launch brisk performs on itself, for one. Silence is correct
        // there: the caller reads the exit code, not the screen.
        if (!AttachConsole(AttachParentProcess)) return;

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
    }
}
