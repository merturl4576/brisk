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
using BriskEngine.Diagnostics;
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
    /// apart by hue first, with a brightness floor underneath.
    ///
    /// The first capture is the control, and it is what stops a zero in the
    /// second from being a zero about the wrong place: the same band, in the
    /// picture where the sensor did speak, is full of ink. The RAM count in
    /// the second image is the other half of that — the instrument is still
    /// drawing what it DID measure in the very frame where it draws nothing
    /// for what it did not.
    ///
    /// Zero, not "fewer". A zero-length arc and a stub left by a rounding
    /// error are both ink, and both are pictures of a measurement that does
    /// not exist. A FAINT arc is the one this count cannot answer for:
    /// IsArcInk's brightness floor is what keeps the health ring's glow out
    /// of the band, and ink dimmer than that floor hides behind it. That case
    /// belongs to the tree assertion above, which sees the element rather
    /// than its pixels — and the fix is not a lower floor, which would start
    /// counting the glow.
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
    /// hue and by brightness. The panel behind it is a near-black navy: at
    /// its brightest stop it leads with blue by 40 levels, which the first
    /// comparison rejects by exactly nothing, and it sits at B 52 against a
    /// floor of 100. The health ring bleeds a glow inward, and its three
    /// colours are the reason the two hue comparisons are BOTH here rather
    /// than one: amber and red are red-led and fail the first, but Good is
    /// #4ADE80, which clears "blue beats red by 40" on its own — and is
    /// stopped dead by blue having to beat green as well. On navy the hue
    /// pair alone would not finish the job: a 10% bleed of Good composites
    /// to #12373C, which passes both of them, and only the brightness floor
    /// leaves it out. That is why the floor does not come down. Only a lit
    /// turquoise stroke leads with blue on every count, brightly.
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

    /// The panel language, on the pages it was supposed to reach. This is
    /// where that bet is settled, and they do not settle it the same way.
    /// Sağlık, Performans and Gizlilik get it for nothing: their cards ARE
    /// FindingCard and CompletionReport, both of which live in Shared.xaml,
    /// so the panel reached them without a line moving on any of the three.
    /// Depolama writes its own three cards, and had to be pointed at the
    /// panel by hand — a style attribute each, no structure touched, but not
    /// free, and this is the image that says which of the two stories a page
    /// is telling.
    ///
    /// Gizlilik is the one that also writes a card of its own: the read-back
    /// lines are not findings and there was no template for them, so they
    /// take CockpitPanel directly. Its row here is what says whether that
    /// card joined the language or quietly went its own way.
    ///
    /// The nav TILE is set rather than the page's Visibility, because that is
    /// the only route the app itself has — Nav_Checked is what shows a page,
    /// and a test reaching past it into Visibility would photograph a state
    /// the running app cannot be in.
    [Theory]
    [InlineData("NavHealth", "HealthView", "page-health")]
    [InlineData("NavPerf", "PerfView", "page-performance")]
    [InlineData("NavClean", "CleanView", "page-storage")]
    [InlineData("NavPrivacy", "PrivacyView", "page-privacy")]
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

    /// The Gizlilik page is the tallest thing in the app — three blocks and
    /// ten possible cards — and at the window's own 700 px the third block is
    /// below the fold. page-privacy.png stays at that size so it can be
    /// compared with its siblings; this is the same page in a frame tall
    /// enough for a human to read the whole of it, which is the only way the
    /// read-back block gets looked at at all.
    ///
    /// The PAGE is photographed rather than the window, and that is forced
    /// rather than chosen: a shown window is clamped by Windows to the
    /// monitor it is on, so the first version of this asked for 1500 px,
    /// got a 1085 px cockpit with 400 px of white under it, and cut the last
    /// read-back line off anyway. A page on the atmosphere has no HWND and no
    /// ceiling — the same reason OverviewPage_LaysOutAndRendersSomething
    /// photographs a page rather than a window. What it costs is the nav and
    /// the title bar, which page-privacy.png has.
    ///
    /// The assertions are about what only this frame can see: a read-back
    /// line per journal entry, each with a sentence under its title, and the
    /// Recall row's link to Windows' own setting — that it exists, that it
    /// reached the picture, and that it says something — which lives with the
    /// card's other actions behind the fold. The read-back count comes from
    /// the view model rather than from a number written here, so a fixture
    /// that grows a fifth line does not need this edited — what would fail is
    /// the block going empty or losing its text.
    /// Enough for the fixture's eleven cards — one of them open — and its
    /// headings with room to spare. Not a magic number to be tuned when the
    /// picture crops: the assertions inside the capture are what fail if
    /// something stops reaching the image, and the answer to that is a taller
    /// frame, not a smaller claim.
    private const int TallEnoughForTheWholePage = 1500;

    [Fact]
    public void ThePrivacyPage_PhotographsAllThreeBlocks()
    {
        PrivacyViewModel? vm = null;
        var path = SnapshotRenderer.Capture(
            () =>
            {
                // The window is built for its composition, not to be
                // shown: it is what wires a PrivacyViewModel the way
                // App.xaml.cs does, in everything the picture depends on. It
                // is NOT the same wiring in two arguments, and both are
                // stated where they are passed — a frozen clock, so "3 days
                // ago" is a fact about the fixture, and a stub opener, so a
                // render cannot start the real Settings app. A second
                // PrivacyPage over the same view model is what gets
                // photographed.
                var window = CockpitWindow();
                vm = (PrivacyViewModel)
                    ((FrameworkElement)window.FindName("PrivacyView")!).DataContext;
                // The Recall card, opened — through the same call the
                // revelation band's link uses, which is the only route the
                // app itself has to open a card. Its link to Windows' own
                // setting lives with the other actions behind the fold, so a
                // photograph of closed cards is a photograph that cannot say
                // whether the link is there at all.
                vm.ExpandFinding("recall-status");
                var page = new PrivacyPage();
                page.Bind(vm);
                return OnAtmosphere(page);
            },
            new Size(1100, TallEnoughForTheWholePage), "page-privacy-full",
            inspect: element =>
            {
                Assert.True(vm!.ReadBackRows.Count > 0,
                    "the read-back block has no lines at all, so the third " +
                    "block of this page is a heading over nothing");

                // Visibility AND a laid-out height, not IsVisible: nothing
                // here is attached to a shown window, and IsVisible answers
                // false for every element of a tree with no presentation
                // source whatever the layout did. Visibility alone would
                // count a collapsed block's text; a block with height is one
                // that was measured, arranged and had somewhere to draw.
                var drawn = Descendants(element).OfType<TextBlock>()
                    .Where(t => t.Visibility == Visibility.Visible
                        && t.ActualHeight > 0)
                    .Select(t => t.Text)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var row in vm.ReadBackRows)
                {
                    Assert.True(drawn.Contains(row.Title),
                        $"'{row.RuleId}' is in the read-back block and its " +
                        "title is nowhere in the picture");
                    Assert.True(drawn.Contains(row.Text),
                        $"'{row.RuleId}' is in the read-back block and its " +
                        $"{row.State} sentence is nowhere in the picture");
                }

                // The spec's Recall sentence, in the frame. Found by the
                // COMMAND OBJECT rather than by its caption: the row and the
                // control are then provably the same one, and the assertion
                // says nothing about which language the picture was taken in.
                var recall = vm.DisclosureRows
                    .Single(r => r.RuleId == "recall-status");
                var link = Descendants(element)
                    .OfType<System.Windows.Controls.Button>()
                    .SingleOrDefault(b =>
                        ReferenceEquals(b.Command, recall.OpenWindowsSettingCommand));
                Assert.True(link is not null,
                    "no control in the picture is bound to the Recall row's " +
                    "link to Windows' own setting, and the spec requires that " +
                    "row to show state only WITH one");
                Assert.True(link!.Visibility == Visibility.Visible
                    && link.ActualWidth > 0,
                    "the Recall row's link is in the tree and not in the " +
                    "picture: it is " + link.Visibility + " and " +
                    link.ActualWidth + " units wide");
                // And that it says something. The caption comes from the same
                // map entry as the URI now, so an entry with no caption key
                // renders a button that is visible, clickable, correctly
                // wired and BLANK — which no visibility or width check can
                // see.
                Assert.Equal(recall.WindowsSettingCaption, link.Content as string);
                Assert.NotEqual("", recall.WindowsSettingCaption);
            });

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"the tall privacy render has {colors} distinct colours — the " +
            "page photographed as a flat fill");
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
                }.Concat(PrivacyTopic()).ToArray(),
                new SensorStatus(false, false, null),
                ReRead(),
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
        var privacy = new PrivacyViewModel(state, host, loc, notDryRun,
            // The render must not start the real Settings app, and nothing
            // here clicks — what this argument buys the picture is the LINK,
            // which the row withholds when nobody is wired to open it.
            _ => true,
            // A frozen clock, so "3 days ago" in the photograph is a fact
            // about the fixture and not about the day the picture was taken.
            () => new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc));

        // EVERY view model above this line, and not one below it. A page
        // populates itself from AppState.Changed, so one built after the scan
        // has subscribed to an event that has already been raised and
        // photographs empty — measured, the first time this one was built
        // three lines lower: the panel count on the Gizlilik render came back
        // 0 and the image was a blank page under a live nav.
        //
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
            clean, privacy, settingsVm, new ThemeManager());
    }

    /// The privacy topic, as ONE COHERENT MACHINE rather than as a list of
    /// every row the page can draw.
    ///
    /// A telemetry switch reports a finding exactly while it reads as ON, and
    /// the read-back below speaks about the journal — so a switch can be a
    /// finding here and Reverted there, and it cannot be a finding here and
    /// Held, WrittenButIgnored or WrittenButUnverified there. A fixture that
    /// ignored that would photograph a machine that cannot exist, and the
    /// picture is what this whole harness is for.
    ///
    /// With six switches the two cannot both be filled to the brim: three
    /// switches read as on here, which leaves three for the three read-back
    /// states that require a switch reading off. What that costs is the
    /// second card in the costly tier — "Timeline ends" is not in this
    /// photograph, and ACostlySwitch_NamesWhatItCosts asserts it for both
    /// rules instead.
    private static DiagnosticFinding[] PrivacyTopic() => new[]
    {
        Disclosure("run-history", "1284"),
        Disclosure("usb-history", "47"),
        Unreadable("delivery-optimization"),
        RecallOff(),
        TestData.Finding("advertising-id", Severity.Info, RuleCategory.Auto,
            stars: 1, canFix: true, kind: FindingKind.Notice),
        TestData.Finding("speech-typing", Severity.Info, RuleCategory.Auto,
            stars: 1, canFix: true, kind: FindingKind.Notice),
        // Reads as on, and the read-back says brisk turned it off once: this
        // is the Reverted machine, and the two halves are the same fact.
        TestData.Finding("location", Severity.Info, RuleCategory.Confirm,
            stars: 1, canFix: true, kind: FindingKind.Notice),
    };

    /// The same rule on a machine whose read found nothing. Two things make
    /// it that: no Headline — the disclosure family's own way of saying it
    /// has no reading to lead with, and what routes it into the page's
    /// "could not read" band — and the rule's OWN unread title key, which is
    /// a different sentence from its readable one. A fixture that kept the
    /// readable title photographed "Windows uploaded data from this machine
    /// to other machines this month" under the heading saying brisk could not
    /// read it: a claim in the picture that the real page never makes.
    private static DiagnosticFinding Unreadable(string ruleId) => new(
        ruleId, $"rule.{ruleId}.title.unread", $"unread {ruleId}",
        $"evidence {ruleId}", Severity.Info, RuleCategory.Advise,
        ImpactStars: 1, CanFix: false, FixDescription: null,
        EvidenceKey: $"rule.{ruleId}.evidence.unread", EvidenceArgs: null,
        Headline: null, Kind: FindingKind.Notice);

    private static DiagnosticFinding Disclosure(string ruleId, string value) =>
        TestData.Finding(ruleId, Severity.Info, RuleCategory.Advise, stars: 1,
            canFix: false, kind: FindingKind.Notice,
            headline: new Headline(value, $"caption {ruleId}",
                $"rule.{ruleId}.headline.value", new[] { value },
                $"rule.{ruleId}.headline.caption", Array.Empty<string>()));

    /// Recall, read the way the rule reads it when the policy is set to
    /// switch the analysis off — and the one row in this fixture that has to
    /// be spelled out rather than built by the helper above.
    ///
    /// RecallStatusRule writes THREE title keys and no "rule.recall-status
    /// .title", because what the row says depends on what the value read as;
    /// its headline value is a WORD and its key carries the same suffix. The
    /// generic helper builds the flat keys, which this rule never produces,
    /// so a card built that way would photograph the engine's English
    /// fallback under a heading of localized ones — the same defect the
    /// unreadable row had, in a different family.
    ///
    /// It is in the picture at all because of the link: this is the one row
    /// on the page whose action opens Windows' own Settings app instead of
    /// changing something, and an element the spec mandates that no image
    /// shows is an element nobody has looked at.
    private static DiagnosticFinding RecallOff() => new(
        "recall-status", "rule.recall-status.title.off",
        "Recall's data analysis is switched off by policy on this machine",
        "evidence recall-status", Severity.Info, RuleCategory.Advise,
        ImpactStars: 1, CanFix: false, FixDescription: null,
        EvidenceKey: "rule.recall-status.evidence.off", EvidenceArgs: null,
        Headline: new Headline("Off",
            "Recall data analysis — what the policy says",
            "rule.recall-status.headline.value.off", Array.Empty<string>(),
            "rule.recall-status.headline.caption", Array.Empty<string>()),
        Kind: FindingKind.Notice);

    /// What brisk found when it looked again — one line in each of the four
    /// states, so the block is photographed saying all four things rather
    /// than the one a tidy fixture would produce. Each rule is in the state
    /// this build can actually put it in: only diagnostic-level has a second
    /// value brisk reads and never writes, so only it can reach
    /// WrittenButIgnored, and activity-history has none, which is exactly
    /// what WrittenButUnverified is for.
    ///
    /// Every rule here reads as OFF except location, which is a finding above
    /// — see PrivacyTopic for why the two lists have to agree about that.
    private static ReadBackResult[] ReRead() => new[]
    {
        new ReadBackResult("tailored-experiences", ReadBackState.Held,
            new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc)),
        new ReadBackResult("location", ReadBackState.Reverted,
            new DateTime(2026, 8, 2, 9, 0, 0, DateTimeKind.Utc)),
        new ReadBackResult("diagnostic-level", ReadBackState.WrittenButIgnored,
            new DateTime(2026, 8, 9, 9, 0, 0, DateTimeKind.Utc)),
        new ReadBackResult("activity-history", ReadBackState.WrittenButUnverified,
            new DateTime(2026, 8, 5, 9, 0, 0, DateTimeKind.Utc)),
    };

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
