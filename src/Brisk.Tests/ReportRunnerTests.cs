using System;
using System.IO;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

/// The console face of the card. A real run scans this machine — seconds of
/// work against real hardware — so these drive the seam that takes the model
/// as a parameter. What is worth pinning here is the SHAPE of each answer,
/// not what a scan of the test runner would have found.
public class ReportRunnerTests
{
    private static ReportCardModel Card()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        return ReportCardModel.Build(
            TestData.Snapshot(null, new SensorStatus(true, true, null)),
            Array.Empty<UndoableFix>(), loc);
    }

    /// A path brisk cannot write gets a sentence, the way every other console
    /// verb answers a failure — not the raw stack trace an uncaught exception
    /// prints over the top of a half-finished command. The unwritable path is
    /// a directory that certainly exists: opening one as a file is refused on
    /// every machine, with no permissions to arrange first.
    [Fact]
    public void UnwritablePath_SaysWhy_InsteadOfThrowing()
    {
        var (code, output) = Capture(
            () => ReportRunner.Run(new[] { "report", "--out", Path.GetTempPath() }, Card));

        // 1, not the 2 a rejected argument buys — this has to be the catch
        // around the work, not the argument loop refusing the path outright.
        Assert.Equal(1, code);
        Assert.StartsWith("brisk: ", output);
        Assert.DoesNotContain("   at Brisk.", output);
    }

    /// The model is never asked for when the command line is wrong — a bad
    /// flag must not cost a scan first.
    [Fact]
    public void BadArgument_IsRefusedBeforeAnythingIsScanned()
    {
        var (code, output) = Capture(() => ReportRunner.Run(
            new[] { "report", "--nope" },
            () => throw new InvalidOperationException("the model was built")));

        Assert.Equal(2, code);
        Assert.Contains("bad argument '--nope'", output);
    }

    /// The path on stdout is the whole console contract: it is what makes
    /// 'brisk-app.exe report' composable with everything else a person types.
    [Fact]
    public void WritesTheCard_AndPrintsWhereItWent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"brisk-{Guid.NewGuid():N}.png");
        try
        {
            var (code, output) = Capture(
                () => ReportRunner.Run(new[] { "report", "--out", path }, Card));

            Assert.Equal(0, code);
            Assert.Equal(path, output.Trim());
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static (int Code, string Output) Capture(Func<int> run)
    {
        var stdout = Console.Out;
        var stderr = Console.Error;
        var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            Console.SetError(buffer);
            return (run(), buffer.ToString());
        }
        finally
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
        }
    }
}
