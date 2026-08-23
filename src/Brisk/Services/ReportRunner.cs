using System;
using System.IO;
using Brisk.Localization;
using Brisk.ViewModels;

namespace Brisk.Services;

/// The console face of the report card: scan, build the model, render, print
/// the path. Lives in the Brisk project because WPF does — Brisk.Cli cannot
/// reference this assembly, so brisk-app.exe's entry point routes the verb
/// here before the CLI parser ever sees it.
public static class ReportRunner
{
    public static int Run(string[] args) => Run(args, ScanThisMachine);

    /// The seam a test can reach. A real run scans the machine it is on —
    /// seconds of work against real hardware — so where the model comes from
    /// is a parameter, and everything a caller can actually be wrong about
    /// (which flags are accepted, where the card goes, what a failure looks
    /// like) is provable with a canned one.
    public static int Run(string[] args, Func<ReportCardModel> model)
    {
        string? outPath = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) { outPath = args[++i]; }
            else
            {
                Console.Error.WriteLine($"brisk: bad argument '{args[i]}'");
                return 2;
            }
        }

        // The same shape every other console verb has — Brisk.Cli.Program.Run
        // wraps its whole dispatch this way. This path is not routed through
        // that one (it is answered before Brisk.Cli sees the arguments), so
        // without this an unwritable --out prints a stack trace where every
        // other brisk failure prints a sentence.
        try
        {
            var path = outPath ?? DefaultPath();
            ReportCardRenderer.RenderOnStaThread(model(), path);
            Console.WriteLine(path);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"brisk: {ex.Message}");
            return 1;
        }
    }

    /// The live model: the app's own services, the saved language so a
    /// Turkish install gets a Turkish card, and one real scan.
    private static ReportCardModel ScanThisMachine()
    {
        var composition = AppServices.Build();
        Loc.Instance.SetLanguage(composition.Settings.Language);
        var snapshot = composition.Host.ScanAsync().GetAwaiter().GetResult();
        return ReportCardModel.Build(
            snapshot, composition.Host.ListUndoable(), Loc.Instance);
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "brisk",
        $"brisk-report-{DateTime.Now:yyyyMMdd-HHmm}.png");
}
