using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Tests.Snapshots;
using Brisk.Theming;
using Brisk.ViewModels;
using Brisk.Views;
using Brisk.Windows;
using BriskEngine.Models;
using Xunit;
// WinForms is on in this project, so these six bare names are ambiguous.
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
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
            () => CockpitWindow(), new Size(1100, 700), "window",
            inspect: AssertTheGlassIsLive, settled: TheGlassIsLive);

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"window render has {colors} distinct colours — the whole cockpit " +
            "photographed as a flat fill, which is what a window that never " +
            "laid out looks like");
    }

    /// What DistinctColors cannot say, checked in the one moment it can be:
    /// the live tiles are photographed with READINGS on them.
    ///
    /// This is not hypothetical tidiness. Moving the harness onto a single
    /// shared UI thread broke exactly this and nothing else — the tick awaits
    /// a Task.Run, and on a thread with a real dispatcher the continuation
    /// queues instead of running inline on the pool, so the shutter opened on
    /// four em dashes. Every test in the suite stayed green, because the only
    /// witness was a PNG nobody was reading. The harness now waits for that
    /// continuation; this is what notices if it ever stops.
    private static void AssertTheGlassIsLive(FrameworkElement element)
    {
        Assert.True(TheGlassIsLive(element),
            "the cockpit was photographed with an em dash where the CPU " +
            "reading should be — the window was rendered before its first " +
            "live tick came back, so the image is a picture of a machine " +
            "with no sensors rather than of the canned reading");

        // Present and correct is not the same as photographable. The reading
        // replaces a startup em dash, and NumeralTick fades that change in
        // over 170 ms — so a capture taken just after the value lands gets
        // the right numbers at a quarter opacity, with every other assertion
        // in this file green. OffscreenLayout.Settle parks the tick; this is
        // what notices if it stops.
        foreach (var numeral in Numerals(element))
            Assert.True(numeral.Opacity >= 0.999,
                $"a live numeral was photographed at opacity {numeral.Opacity:F2} " +
                "— the shutter caught NumeralTick's fade part-way through, so " +
                "the readings are in the image but greyed out");
    }

    /// The round's headline honesty claim, settled where it has to be
    /// settled: in the photograph. "Every ring carries real data, and a
    /// sensor that cannot be read gets no ring at all — not an empty one,
    /// not a zero." A flag on a view model cannot say that; only the image
    /// can.
    ///
    /// Two captures of the same cockpit differing in one field — a CPU
    /// percentage, then a machine whose CPU counter has not spoken — and the
    /// arc's own band counted in each. The band is a thin annulus at the
    /// radius SweepRing draws at, centred on the dial, and nothing else
    /// reaches it: RAM rides 7 px inside, the gauge's ticks stop 6 px
    /// outside the centreline, and the health ring's own colours are
    /// red-led or green-led, never blue-led — which is why the ink is told
    /// apart by hue rather than by brightness.
    ///
    /// The first capture is the control, and it is what stops a zero in the
    /// second from being a zero about the wrong place: the same band, in the
    /// picture where the sensor did speak, is full of ink. The RAM count in
    /// the second image is the other half of that — the instrument is still
    /// drawing what it DID measure in the very frame where it draws nothing
    /// for what it did not.
    ///
    /// Zero, not "fewer". A faint arc, a zero-length arc and a stub left by
    /// a rounding error are all ink, and all three are pictures of a
    /// measurement that does not exist.
    ///
    /// The tree assertions inside the captures and the pixel counts after
    /// them are two different claims, and NEITHER covers the other. The tree
    /// assertions are what the Visibility binding answers to: drop that
    /// binding and they go red while the band stays empty, because SweepRing
    /// separately declines to stroke a zero-length sweep — belt as well as
    /// braces, and the counts cannot see the difference. What the counts do
    /// see is anything actually drawn in that band, which is the failure a
    /// tree flag is blind to: an arc given a minimum stub, and an arc
    /// photographed before OffscreenLayout parked it, each put 19 pixels
    /// there, and each turns this red. Both were planted and watched to
    /// fail; so was the dropped binding.
    [Fact]
    public void TheCpuArc_IsAbsentFromThePicture_WhenTheSensorSaidNothing()
    {
        Dial lit = default;
        Dial silent = default;

        var litPath = SnapshotRenderer.Capture(
            () => CockpitWindow(new LiveReading(23.4, 61.2, 47.5, "CPU", 122L << 30)),
            new Size(1100, 700), "instrument-both-arcs",
            inspect: element =>
            {
                lit = DialOf(element);
                AssertTheArcsShowing(element, cpu: true, ram: true);
            },
            settled: TheTickCameBack);

        var silentPath = SnapshotRenderer.Capture(
            () => CockpitWindow(new LiveReading(null, 42.0, 47.5, "CPU", 122L << 30)),
            new Size(1100, 700), "instrument-no-cpu-arc",
            inspect: element =>
            {
                silent = DialOf(element);
                AssertTheArcsShowing(element, cpu: false, ram: true);
            },
            settled: TheTickCameBack);

        var drawn = ArcInk(litPath, lit.Centre, lit.CpuRadius);
        Assert.True(drawn > 100,
            $"the CPU band holds {drawn} lit pixels in the picture where the " +
            "sensor DID speak — so the band is being counted somewhere the arc " +
            "is not, and a zero in the other image would say nothing at all");

        var absent = ArcInk(silentPath, silent.Centre, silent.CpuRadius);
        Assert.Equal(0, absent);

        var ram = ArcInk(silentPath, silent.Centre, silent.RamRadius);
        Assert.True(ram > 100,
            $"the RAM band holds {ram} lit pixels in the same picture — the " +
            "instrument stopped drawing the arc it DID measure, so what the " +
            "CPU band is showing is a dead render rather than an honest one");
    }

    /// Half the stroke plus room for its antialiased edges. Wide enough to
    /// hold all of a 3.5 px arc, narrow enough that the RAM arc 7 px inside
    /// stays out: its outer edge lands 2.75 px short of this band.
    private const double BandHalfWidth = 2.5;

    private static int ArcInk(string path, Point centre, double radius) =>
        SnapshotRenderer.InkInBand(path, centre,
            radius - BandHalfWidth, radius + BandHalfWidth, IsArcInk);

    /// An arc's ink, told from everything else that can reach the band by
    /// hue. The panel behind it is a near-black graphite that leads with
    /// blue by only 12 levels. The health ring bleeds a glow inward, and its
    /// three colours are the reason BOTH comparisons are here rather than
    /// one: amber and red are red-led and fail the first, but Good is
    /// #4ADE80, which clears "blue beats red by 40" on its own — and is
    /// stopped dead by blue having to beat green as well. Only a lit
    /// turquoise stroke leads with blue on both counts, brightly.
    private static bool IsArcInk(Color pixel) =>
        pixel.B > pixel.R + 40 && pixel.B > pixel.G && pixel.B > 100;

    /// Where the two arcs land in a captured image: the dial's centre in the
    /// picture's own coordinates, and each arc's radius taken from ITS OWN
    /// box and the control's own inset. Nothing here is a number copied out
    /// of the XAML — a copy would keep passing about a place the arcs had
    /// moved away from.
    private readonly record struct Dial(Point Centre, double CpuRadius, double RamRadius);

    private static Dial DialOf(FrameworkElement element)
    {
        var page = OverviewPageOf(element);
        var dial = (FrameworkElement)page.FindName("InstrumentDial")!;
        var box = dial.TransformToAncestor(element)
            .TransformBounds(new Rect(0, 0, dial.ActualWidth, dial.ActualHeight));
        return new Dial(
            new Point(box.X + box.Width / 2, box.Y + box.Height / 2),
            RadiusOf(page, "CpuArc"), RadiusOf(page, "RamArc"));
    }

    /// Width, not ActualWidth: a collapsed element has no ActualWidth, and
    /// the whole question here is where an arc WOULD have been drawn.
    private static double RadiusOf(FrameworkElement page, string arc) =>
        ((FrameworkElement)page.FindName(arc)!).Width / 2 - SweepRing.Inset;

    /// The visual-tree half, and the half the Visibility binding answers to:
    /// a silent sensor leaves the arc out of the render entirely, rather
    /// than leaving an element in it that happens to sweep nothing.
    private static void AssertTheArcsShowing(FrameworkElement element, bool cpu, bool ram)
    {
        var page = OverviewPageOf(element);
        Assert.Equal(cpu, ((FrameworkElement)page.FindName("CpuArc")!).IsVisible);
        Assert.Equal(ram, ((FrameworkElement)page.FindName("RamArc")!).IsVisible);
    }

    /// The names on the arcs belong to OverviewPage's namescope, not the
    /// window's, so they are reached through the page rather than off the
    /// window the way OverviewView itself is.
    private static FrameworkElement OverviewPageOf(FrameworkElement element) =>
        (FrameworkElement)((Window)element).FindName("OverviewView")!;

    /// The panel language, on the three pages it was supposed to reach.
    /// This is where that bet is settled, and the three do not settle it the
    /// same way. Sağlık and Performans get it for nothing: their cards ARE
    /// FindingCard and CompletionReport, both of which live in Shared.xaml,
    /// so the panel reached them without a line moving on either page.
    /// Depolama writes its own three cards, and had to be pointed at the
    /// panel by hand — a style attribute each, no structure touched, but not
    /// free, and this is the image that says which of the two stories a page
    /// is telling.
    ///
    /// The nav TILE is set rather than the page's Visibility, because that is
    /// the only route the app itself has — Nav_Checked is what shows a page,
    /// and a test reaching past it into Visibility would photograph a state
    /// the running app cannot be in.
    [Theory]
    [InlineData("NavHealth", "HealthView", "page-health")]
    [InlineData("NavPerf", "PerfView", "page-performance")]
    [InlineData("NavClean", "CleanView", "page-storage")]
    public void APage_PhotographsWearingThePanels(
        string tile, string page, string name)
    {
        var path = SnapshotRenderer.Capture(
            () =>
            {
                var window = CockpitWindow();
                ((RadioButton)window.FindName(tile)!).IsChecked = true;
                return window;
            },
            new Size(1100, 700), name,
            inspect: element => AssertThePanelsAreInThePicture(element, page),
            settled: TheGlassIsLive);

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"the page render has {colors} distinct colours — the whole window " +
            "photographed as a flat fill, which is what a window that never " +
            "laid out looks like");
    }

    /// What DistinctColors cannot say about a page of panels: whether any
    /// panel is actually in it.
    ///
    /// The corner bracket is the one mark the panel draws that nothing else
    /// in the app draws, so counting the Paths carrying the bracket style
    /// counts panels, four to a panel — and VISIBLE ones, because the
    /// completion report is a panel too and it sits collapsed until a fix run
    /// gives it something to say. A page that quietly went back to a plain
    /// bordered card photographs with none of them.
    private static void AssertThePanelsAreInThePicture(
        FrameworkElement element, string page)
    {
        Assert.True(((FrameworkElement)((Window)element).FindName(page)!).IsVisible,
            $"{page} is not the page showing — the nav tile was checked, so " +
            "either Nav_Checked stopped answering it or the window was " +
            "photographed before it did");

        var style = (Style)Application.Current.Resources["PanelBracket"];
        var brackets = Descendants(element)
            .OfType<System.Windows.Shapes.Path>()
            .Count(p => p.IsVisible && ReferenceEquals(p.Style, style));

        Assert.True(brackets >= 4,
            $"the page was photographed with {brackets} bracket strokes showing " +
            "— a panel wears four, so whatever is in this image, the cards on " +
            "it are not wearing the panel");
    }

    /// Every TextBlock in the tree that NumeralTick drives.
    private static IEnumerable<TextBlock> Numerals(DependencyObject root) =>
        Descendants(root).OfType<TextBlock>()
            .Where(numeral => NumeralTick.GetValue(numeral) is not null);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var found in Descendants(VisualTreeHelper.GetChild(root, i)))
                yield return found;
    }

    /// ONE statement of what live means, used twice: as the condition the
    /// capture waits for, and as the claim it is asserted against afterwards.
    /// Two copies would be two chances to drift, and the pair only works if
    /// the thing waited on is the thing checked.
    private static bool TheGlassIsLive(FrameworkElement element) =>
        OverviewOf(element).LiveCpuText != "—";

    /// The first tick came back, whatever the sensors happened to say. Free
    /// disk is the one reading that is never null, so it is the signal that
    /// survives a machine with no CPU counter — where waiting on the CPU tile
    /// would sit out the settle deadline and then photograph a cockpit that
    /// had not ticked at all, which is a picture of the wrong thing.
    private static bool TheTickCameBack(FrameworkElement element) =>
        OverviewOf(element).LiveDiskText != "—";

    private static OverviewViewModel OverviewOf(FrameworkElement element) =>
        (OverviewViewModel)OverviewPageOf(element).DataContext;

    /// The real window with fake machinery behind it: the same composition
    /// App.xaml.cs performs, with the engine, the recycle bin and the
    /// process/registry surfaces replaced. Everything is built on the render
    /// thread, because that is the thread the window will live on.
    /// `reading` is what the live tiles are fed; the default is a machine
    /// with every sensor answering.
    internal static Window CockpitWindow(LiveReading? reading = null)
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
            new CannedLive(reading), loc, notDryRun);
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
    /// than as the em dashes a machine with no sensors would show. A caller
    /// may hand in its own, which is how a machine with a silent sensor gets
    /// photographed on purpose.
    private sealed class CannedLive : ILiveMetrics
    {
        private readonly LiveReading _reading;

        public CannedLive(LiveReading? reading) =>
            _reading = reading ?? new LiveReading(23.4, 61.2, 47.5, "CPU", 122L << 30);

        public bool IsTicking { get; private set; }
        public LiveReading Read() => _reading;

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
