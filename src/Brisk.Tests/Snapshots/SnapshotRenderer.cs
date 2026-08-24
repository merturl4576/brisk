using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    public static string Capture(Func<FrameworkElement> build, Size size, string name)
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
                    OffscreenLayout.LayOut(element, size);

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
    private static void OnStaThread(Action work)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { work(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
