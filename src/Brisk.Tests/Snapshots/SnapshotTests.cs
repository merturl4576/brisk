using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Tests.Snapshots;
using Brisk.Theming;
using Brisk.ViewModels;
using Brisk.Views;
using Brisk.Windows;
using BriskEngine.Models;
using Xunit;
// WinForms is on in this project, so bare Brushes and Size are ambiguous.
using Brushes = System.Windows.Media.Brushes;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The images exist so a human can look at them. What is asserted here is
/// only what can be stated: the page laid out without throwing, and the PNG
/// is not a dead render. "Not dead" is the check that matters — the report
/// card once produced a perfectly valid 312 KB PNG whose subject, the ring,
/// was blank, and a size-only smoke test passed over it.
public class SnapshotTests
{
    [Fact]
    public void OverviewPage_LaysOutAndRendersSomething()
    {
        var path = SnapshotRenderer.Capture(
            () => OnAtmosphere(new OverviewPage()),
            new Size(1100, 700),
            "overview");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"render has {colors} distinct colours — a flat fill means the page " +
            "drew nothing, which is what a dead render looks like");
    }

    /// A Window is a FrameworkElement, so Capture's signature promises this
    /// works. It very nearly did not: a window that was never shown has no
    /// HWND to measure itself against, lays out to nothing, and photographs
    /// as a flat fill without throwing anything at all. That is precisely
    /// the dead render this file exists to refuse, and the whole window is
    /// what the cockpit shell gets judged on — so the promise is pinned
    /// here, on the smallest window that can hold ink.
    [Fact]
    public void Window_LaysOutAndRendersItsContent()
    {
        var path = SnapshotRenderer.Capture(
            () => new Window
            {
                Width = 320,
                Height = 180,
                Background = Brushes.Black,
                Content = new TextBlock
                {
                    Text = "brisk",
                    FontSize = 56,
                    Foreground = Brushes.White,
                    Margin = new Thickness(20),
                },
            },
            new Size(320, 180),
            "window-probe");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"window render has {colors} distinct colours — an unshown window " +
            "lays out against an HWND it does not have, so it photographs " +
            "blank rather than failing");
    }

    /// The cockpit itself: the real MainWindow, at the size it now opens at,
    /// with a scan already run so the ring has a score and the pages have
    /// rows. Every earlier image in this folder was a page on a bare
    /// atmosphere; this one is the shell — our own title bar, the nav tiles
    /// floating with no rail under them, and the page standing in the space
    /// that is left.
    ///
    /// DistinctColors is a weak assertion here and deliberately named as one:
    /// the atmosphere alone clears 16 colours, so this cannot tell a lit ring
    /// from a dead grey one. The image is for human eyes; what is asserted is
    /// that the window laid out and drew.
    [Fact]
    public void MainWindow_LaysOutAndRendersTheWholeCockpit()
    {
        var path = SnapshotRenderer.Capture(
            CockpitWindow, new Size(1100, 700), "window");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"window render has {colors} distinct colours — the whole cockpit " +
            "photographed as a flat fill, which is what a window that never " +
            "laid out looks like");
    }

    /// The real window with fake machinery behind it: the same composition
    /// App.xaml.cs performs, with the engine, the recycle bin and the
    /// process/registry surfaces replaced. Everything is built on the render
    /// thread, because that is the thread the window will live on.
    private static Window CockpitWindow()
    {
        var host = new FakeEngineHost
        {
            NextSnapshot = TestData.Snapshot(
                new[]
                {
                    TestData.Finding("power-plan", cat: RuleCategory.Auto, canFix: true),
                    TestData.Finding("startup-heavy", cat: RuleCategory.Auto, canFix: true),
                    TestData.Finding("thermals", Severity.Info, RuleCategory.Advise,
                        canFix: false),
                },
                TestData.Target("user-temp", CleanupLevel.Safe, 2048)),
        };
        var loc = new Loc();
        loc.SetLanguage("en");
        var settings = new Settings();
        var state = new AppState(host, loc);
        var fixAll = new FixAllService(host);
        var cleanService = new CleanService(host, settings);
        var bin = new FakeBin();
        var safeClean = new SafeCleanRunner(cleanService, bin);
        var launcher = new StartupLauncher(new FakeProcessRunner(), new FakeRegistry(),
            @"C:\brisk\brisk.exe");
        Func<bool> notDryRun = () => false;

        var overview = new OverviewViewModel(state, host, fixAll, safeClean,
            new CannedLive(), loc, notDryRun);
        var health = new HealthViewModel(state, host, loc, notDryRun, fixAll,
            FindingSections.IsHealth, doneFilter: FindingSections.IsHealth,
            crossLinkKey: "health.crosslink");
        var performance = new HealthViewModel(state, host, loc, notDryRun, fixAll,
            FindingSections.IsPerformance, doneFilter: FindingSections.IsPerformance,
            crossLinkKey: "performance.crosslink");
        var startup = new StartupViewModel(state, host, loc, notDryRun, launcher);
        var clean = new CleanViewModel(state, host, cleanService, safeClean, bin,
            loc, notDryRun);
        var settingsVm = new SettingsViewModel(settings,
            Path.Combine(Path.GetTempPath(), "brisk-snapshot-settings.json"),
            launcher, _ => { }, _ => { });

        // A window photographed before the scan is a picture of an empty
        // cockpit — no score on the ring, no rows on any page. AppState's
        // scan yields once, and this thread has no message loop to resume it
        // on, so the synchronization context is cleared for the duration: the
        // continuation lands on the thread pool instead of on a dispatcher
        // that will never pump. Nothing is data-bound yet, so the view models
        // it wakes are still plain objects when it touches them.
        var saved = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try { state.ScanAsync().GetAwaiter().GetResult(); }
        finally { SynchronizationContext.SetSynchronizationContext(saved); }

        return new MainWindow(state, overview, health, performance, startup,
            clean, settingsVm, new ThemeManager());
    }

    /// One canned reading, so the live tiles photograph as numbers rather
    /// than as the em dashes a machine with no sensors would show.
    private sealed class CannedLive : ILiveMetrics
    {
        public bool IsTicking { get; private set; }
        public LiveReading Read() => new(23.4, 61.2, 47.5, "CPU", 122L << 30);

        public void Start(Action onTick)
        {
            if (IsTicking) return;
            IsTicking = true;
            onTick();
        }

        public void Stop() => IsTicking = false;
    }

    /// A page is photographed the way the window shows it: standing on the
    /// atmosphere. The layer belongs to the WINDOW, so a page rendered by
    /// itself has nothing underneath it and comes out floating on the void —
    /// and the ground is exactly what this round gets judged on. The brushes
    /// are pulled by resource reference rather than left on the layer's own
    /// defaults, which is what MainWindow's {DynamicResource} does, so the
    /// image is of the real thing and not of a lookalike.
    private static FrameworkElement OnAtmosphere(UIElement page)
    {
        var layer = new AtmosphereLayer();
        layer.SetResourceReference(AtmosphereLayer.IsFlatProperty, "FlatAtmosphere");
        layer.SetResourceReference(AtmosphereLayer.SkyBrushProperty, "Bg0");
        layer.SetResourceReference(AtmosphereLayer.GroundBrushProperty, "Bg");
        layer.SetResourceReference(AtmosphereLayer.TextureBrushProperty, "AccentDim");
        layer.SetResourceReference(AtmosphereLayer.GlowBrushProperty, "AccentGlow");

        var grid = new Grid();
        grid.Children.Add(layer);
        grid.Children.Add(page);
        return grid;
    }
}
