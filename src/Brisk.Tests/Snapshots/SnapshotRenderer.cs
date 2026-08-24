using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brisk.Views;
// WinForms is on in this project, so bare Application and Size are ambiguous.
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace Brisk.Tests.Snapshots;

/// Photographs a piece of the real cockpit into the repo's .snapshots folder,
/// so the design can be judged from what WPF actually draws instead of from a
/// mockup. The images are for human eyes; the tests around them only assert
/// that a render happened at all.
public static class SnapshotRenderer
{
    /// Deliberately NOT the report card's 2x/192 DPI. The card is rendered
    /// large so it survives a platform's recompression; these images exist to
    /// be compared with each other across sessions, so their scale must never
    /// move.
    private const double Dpi = 96;

    /// Far enough left of any real monitor arrangement that a shown window
    /// cannot appear on one.
    private const double Offscreen = -10000;

    /// One process, one Application, one render at a time. WPF's parse-time
    /// {StaticResource} lookups fall through to Application.Current.Resources
    /// — that is the only place a control's own XAML can reach — so the theme
    /// has to be installed there before anything is built, and an Application
    /// is a once-per-AppDomain object. Holding the lock across the whole
    /// capture also keeps two parallel test classes from rendering at once.
    private static readonly object Gate = new();

    private static bool _themeInstalled;

    /// The harness's one UI thread. Written on that thread and read by
    /// callers, but every read and write happens under Gate, which is the
    /// barrier — OnStaThread is only ever reached through Capture or
    /// OnUiThread, and both hold the lock across the whole call.
    private static Dispatcher? _ui;

    /// `inspect` runs on the UI thread with the element laid out and about to
    /// be photographed. It exists because "the image is not dead" is a weak
    /// thing to assert about a picture of a cockpit: a caller that cares
    /// whether a particular reading made it onto the glass can look, in the
    /// one moment where looking answers the question.
    /// `settled` is what the capture WAITS for; `inspect` is what it
    /// asserts. A caller with async work behind it supplies both, built from
    /// one predicate, so the thing waited on and the thing claimed cannot
    /// drift apart.
    public static string Capture(Func<FrameworkElement> build, Size size, string name,
        Action<FrameworkElement>? inspect = null,
        Func<FrameworkElement, bool>? settled = null)
    {
        var path = Path.Combine(SnapshotDir(), name + ".png");
        lock (Gate)
        {
            OnStaThread(() =>
            {
                InstallTheme();
                var element = build();
                // A Window sizes its content from the HWND underneath it, and
                // a window that was never shown has none: it lays out to
                // nothing and photographs as a flat fill, WITHOUT throwing.
                // Showing it is what gives it that handle. It goes far off
                // any real desktop and never takes the taskbar or the focus,
                // so a test run does not flash a window or steal the keyboard
                // out from under whoever is typing.
                var window = element as Window;
                try
                {
                    if (window is not null)
                    {
                        window.Left = Offscreen;
                        window.Top = Offscreen;
                        window.ShowInTaskbar = false;
                        window.ShowActivated = false;
                        window.Show();
                    }
                    // Everything Show() set in motion has to finish before
                    // the shutter opens — see PumpUntilSettled.
                    PumpUntilSettled(element, settled);
                    OffscreenLayout.LayOut(element, size);

                    inspect?.Invoke(element);

                    var bitmap = new RenderTargetBitmap(
                        (int)size.Width, (int)size.Height, Dpi, Dpi, PixelFormats.Pbgra32);
                    bitmap.Render(element);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));

                    using var stream = File.Create(path);
                    encoder.Save(stream);
                }
                finally
                {
                    // In the finally so a render that throws cannot leave a
                    // live window behind on the STA thread.
                    //
                    // Close() is a REQUEST, and brisk's own MainWindow refuses
                    // it: the app lives in the tray, so OnClosing cancels and
                    // hides instead. That left a live HWND owned by a thread
                    // about to end, and the OS then tore it down with no
                    // dispatcher pumping and no WPF shutdown — which crashed
                    // the test host, intermittently, several tests later.
                    // Disposing the HwndSource destroys the window properly
                    // while the thread is still alive; it is a no-op for a
                    // window that did close, because a closed window has no
                    // presentation source left to find.
                    window?.Close();
                    if (window is not null)
                        (PresentationSource.FromVisual(window) as HwndSource)?.Dispose();
                }
            });
        }
        return path;
    }

    /// The same STA thread, the same installed theme and the same lock as
    /// Capture — for a test that needs to DRIVE a piece of the cockpit rather
    /// than photograph it. It shares the gate because it shares the
    /// Application: two WPF tests running at once would be building controls
    /// against one resource dictionary from two threads.
    public static void OnUiThread(Action work)
    {
        lock (Gate)
        {
            OnStaThread(() =>
            {
                InstallTheme();
                work();
            });
        }
    }

    /// How many distinct 32-bit pixel values the PNG holds. A render that drew nothing
    /// is a flat fill — one colour, or a handful from a background gradient —
    /// and that is the failure this exists to catch: the report card once
    /// shipped a perfectly valid 312 KB PNG whose whole subject was blank.
    public static int DistinctColors(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None,
            BitmapCacheOption.OnLoad);
        var pixels = new int[frame.PixelWidth * frame.PixelHeight];
        frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
        return new HashSet<int>(pixels).Count;
    }

    /// The theme dictionaries, in the order ReportCard.xaml documents as the
    /// contract: Dark first, because Shared's Hero* family is built on the
    /// base palette Dark defines. The ";component" URI names brisk-app
    /// explicitly — the short form resolves against the ENTRY assembly, which
    /// under the test host is the runner, not the app.
    ///
    /// The card can merge its dictionaries into its own XAML because it is
    /// rendered as itself; a page cannot be given resources from out here,
    /// because its {StaticResource} lookups are resolved while its
    /// constructor is still running. Application.Current.Resources is the
    /// one dictionary that is already in scope by then. Idempotent, and
    /// called under Gate: the Application is a once-per-AppDomain object.
    private static void InstallTheme()
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        // The constructor is the registration — Application.Current is what
        // everything downstream reads, so the instance itself is not kept.
        if (Application.Current is null) _ = new Application();
        if (_themeInstalled) return;
        foreach (var file in new[] { "Dark.xaml", "Shared.xaml" })
            Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(
                    $"pack://application:,,,/brisk-app;component/Theming/{file}"),
            });
        _themeInstalled = true;
    }

    private static string SnapshotDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Directory.CreateDirectory(
                    Path.Combine(dir.FullName, ".snapshots")).FullName;
        throw new InvalidOperationException("brisk.sln not found above test bin");
    }

    /// WPF objects demand an STA thread and the test runner does not have one
    /// — the same reason ReportCardRenderer.RenderOnStaThread exists.
    ///
    /// ONE thread, for the life of the test process. It used to be a fresh
    /// thread per call, and that was a latent fault rather than a style
    /// choice: a WPF object belongs to the thread that made it, and the
    /// windows built here OUTLIVE the call that built them. Every MainWindow
    /// subscribes the process-lifetime Loc.Instance singleton and never
    /// unsubscribes, so each capture left a live window reachable from a
    /// static field on a thread that had already exited. The next
    /// Loc.SetLanguage — CaptionButtonTests raises it on purpose,
    /// LocKeyConverterTests in passing — walked that subscriber list
    /// synchronously and read WindowState on a dead foreign thread, and
    /// VerifyAccess threw into whichever unrelated test was running at the
    /// time. The suite was green on scheduling order, not on design: adding
    /// two more window captures killed the run at test 487 of 502, and the
    /// stack pointed at a test that had nothing to do with it.
    ///
    /// A single shared thread makes every window this harness has ever built
    /// a peer of every other one, so the harness's own raises are in-thread.
    /// The other half of the fix is in MainWindow, which now marshals that
    /// handler instead of trusting whatever thread the singleton was poked
    /// from.
    private static void OnStaThread(Action work)
    {
        Exception? failure = null;
        UiThread().Invoke(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });
        if (failure is not null) throw failure;
    }

    /// The hard cap on waiting, and nothing more than that. The normal path
    /// does not spend it — the loop leaves the moment the caller's condition
    /// is true — so this is sized for the worst case rather than the usual
    /// one: xunit saturates the thread pool, and a continuation coming back
    /// from a starved pool can take far longer than the work inside it. A
    /// budget tuned to how fast an idle laptop happens to be is how shared
    /// infrastructure starts flaking, and seven tasks depend on this one.
    private const int SettleDeadlineMs = 2000;

    /// How long to wait before draining again while the condition is still
    /// false. Short, because it only costs anything when the pool is slow.
    private const int SettlePollMs = 5;

    /// The longest a single drain may run before it gives the loop control
    /// back. This is a safety valve, not a budget — see DrainToIdle.
    private const int DrainSliceMs = 250;

    /// Runs the work the element queued in response to being shown, before
    /// the picture is taken.
    ///
    /// The live tiles are why this exists. OverviewViewModel's tick awaits a
    /// Task.Run, so the readings come back on a continuation posted to this
    /// dispatcher — and a capture that renders without letting that
    /// continuation run photographs em dashes where the numbers should be.
    ///
    /// It used to work by accident. The old throwaway thread never ran a
    /// dispatcher loop, so there was no SynchronizationContext to post to,
    /// the continuation ran inline on the pool thread, and it beat layout to
    /// the properties. On a real UI thread the continuation queues, which is
    /// what a UI thread is supposed to do — so the harness waits for it on
    /// purpose instead of relying on the absence of a message pump.
    ///
    /// CONDITION-bounded, not time-bounded, and the difference is the whole
    /// point. With no condition there is nothing to wait for beyond an empty
    /// queue, and one drain says that. With one, the loop exits the moment it
    /// holds, so the deadline above is only ever reached when something is
    /// genuinely wrong. Running out of time is deliberately SILENT: the
    /// caller's inspect assertion is what names the failure, because the
    /// harness photographs and the test decides what counts as a picture.
    private static void PumpUntilSettled(
        FrameworkElement element, Func<FrameworkElement, bool>? isSettled)
    {
        var deadline = Environment.TickCount64 + SettleDeadlineMs;
        while (true)
        {
            var idle = DrainToIdle();
            // With a condition, the condition decides. Without one there is
            // nothing to wait for except the queue emptying, so an idle pass
            // IS the answer — and a pass that was cut short is not, which is
            // why DrainToIdle reports which of the two happened.
            if (isSettled is null ? idle : isSettled(element)) return;
            if (Environment.TickCount64 >= deadline) return;
            Thread.Sleep(SettlePollMs);
        }
    }

    /// Lets the dispatcher run everything queued above ApplicationIdle, and
    /// says whether it got there. A nested frame rather than an Invoke,
    /// because this runs INSIDE the dispatcher operation doing the capture:
    /// the queue cannot drain until that operation lets it.
    ///
    /// The timer is not a refinement, it is the difference between a bounded
    /// wait and a hung test run. A shown cockpit animates forever — the
    /// overview's orbit, comet and sheen spin for as long as the window is up
    /// — and every animation frame posts a RENDER-priority operation. Render
    /// outranks ApplicationIdle, so on a window like that the queue never
    /// falls idle, the marker below is never reached, and PushFrame spins
    /// until the process is killed. I hung a full suite run exactly this way.
    /// Send priority is above Render, so the clock always gets through.
    private static bool DrainToIdle()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var reachedIdle = false;

        dispatcher.BeginInvoke(
            (Action)(() => { reachedIdle = true; frame.Continue = false; }),
            DispatcherPriority.ApplicationIdle);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(DrainSliceMs),
            DispatcherPriority.Send, (_, _) => frame.Continue = false, dispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }

        return reachedIdle;
    }

    /// Starts the UI thread on first use and hands back its dispatcher.
    private static Dispatcher UiThread()
    {
        if (_ui is not null) return _ui;

        var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _ui = Dispatcher.CurrentDispatcher;
            ready.Set();
            // The pump, and it is not optional: Invoke above would otherwise
            // queue work that nothing ever runs, and a window that is Shown
            // would never process a single message.
            Dispatcher.Run();
        })
        {
            // Never shut down — the windows parked on it must stay reachable
            // on a LIVE thread, which is the whole point. Background, so a
            // dispatcher that is deliberately immortal cannot outlive the
            // test host and hang the run.
            IsBackground = true,
            Name = "brisk-snapshot-ui",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return _ui!;
    }
}
