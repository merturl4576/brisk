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
    public static int Run(string[] args)
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

        var composition = AppServices.Build();
        Loc.Instance.SetLanguage(composition.Settings.Language);
        var snapshot = composition.Host.ScanAsync().GetAwaiter().GetResult();
        var model = ReportCardModel.Build(
            snapshot, composition.Host.ListUndoable(), Loc.Instance);
        var path = outPath ?? DefaultPath();
        ReportCardRenderer.RenderOnStaThread(model, path);
        Console.WriteLine(path);
        return 0;
    }

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "brisk",
        $"brisk-report-{DateTime.Now:yyyyMMdd-HHmm}.png");
}
