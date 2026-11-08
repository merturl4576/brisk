using System.IO;
using System.Windows;
using System.Windows.Controls;
using BriskEngine.Actions;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Tests.Ui;
using Brisk.Ui.Controls;
using Brisk.Ui.Home;
using Brisk.Ui.Pages;
using Size = System.Windows.Size;

namespace Brisk.Tests.Snapshots;

/// Photographs of the new window, for my eyes, in .snapshots/new-*.png.
/// What the tests assert is that each state renders and binds cleanly.
public sealed class NewGuiSnapshotTests
{
    private static readonly Size Canvas = new(1100, 680);

    internal static FakeBackend Rich()
    {
        var b = FakeBackend.Typical();
        // The maintainer's laptop as Home reads it (specs verified live
        // 2026-11-07; the load numbers are the mockup's): the tallest columns
        // Home draws — a battery with health and cycles, two GPUs, a CPU
        // name that wraps.
        b.SpecsToReturn = new SpecsInfo("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", 2592, 6, 12,
            new[] { "Intel(R) UHD Graphics", "NVIDIA GeForce GTX 1650 Ti" }, 32L << 30, 34_118_000_000,
            "KINGSTON SKC3000S1024G", 1_024_209_543_168);
        b.MonitorToReturn = new MonitorSample(20, 12, 58, 19_756_000_000, 66, 52, 10_375, 14_000,
            new BatteryInfo(100, true, 29, 312),
            new[] { new ProcLoad("Claude Code", 9), new ProcLoad("Google Chrome", 6), new ProcLoad("Visual Studio Code", 4) },
            new[] { new ProcLoad("Google Chrome", 8), new ProcLoad("LivelyWallpaper", 6), new ProcLoad("Desktop Window Manager", 4) },
            new[] { new ProcLoad("Visual Studio Code", 6_013_000_000), new ProcLoad("Claude Code", 3_113_000_000), new ProcLoad("Wispr Flow", 634L << 20) });
        return b;
    }

    /// The real catalog's rows, so pages photograph with real titles and costs.
    internal static FakeBackend Catalog()
    {
        var b = Rich();
        ActionItem R(string id, long? bytes = null, IReadOnlyList<ActionFile>? files = null) =>
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), bytes, files: files);
        ActionItem Startup(string name, bool on = true, string? cost = null) => new(
            $"startup:HKCU:{name}", ActionGroupKind.Speed, null, name, cost is null ? null : "x", cost,
            ActionCatalog.StartupNameKey, ActionCatalog.StartupName, null, on, false, false, true,
            ActionLinkKind.Rules, new[] { $"startup:HKCU:{name}" }, Array.Empty<ActionFile>());
        var files = Enumerable.Range(1, 6).Select(i => new ActionFile($@"C:\Users\m\Downloads\setup-{i}.exe",
            (900L - i * 120) << 20, new DateTime(2026, 6, i, 0, 0, 0, DateTimeKind.Utc))).ToList();
        b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            R("space.caches", 6_013_000_000), R("space.developer", 8_050_000_000),
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.shaders"), 2_470_000_000),
            R("space.downloads", files.Sum(f => f.Bytes), files), R("space.recycle-bin", 8_800_000_000),
        });
        b.Fast = new List<ActionGroup>
        {
            new(ActionGroupKind.Speed, new[]
            {
                Startup("Microsoft Edge"), Startup("Spotify"), Startup("Discord"),
                Startup("OneDrive", on: false, cost: "File sync stops."),
                R("speed.power-mode"), R("speed.animations"), R("speed.gpu-scheduling"),
            }),
            new(ActionGroupKind.Privacy, new[]
            {
                R("privacy.telemetry-services"), R("privacy.ads"), R("privacy.activity"),
                R("privacy.start-web"), R("privacy.copilot"), R("privacy.location"),
            }),
        };
        return b;
    }

    internal static GroupPageViewModel Page(FakeBackend b, ActionGroupKind group, bool run = false, string language = "tr")
    {
        var (home, session) = Home(b, language);
        if (run) session.RunAllAsync().GetAwaiter().GetResult();
        var loc = home.L;
        var page = new GroupPageViewModel(session, group, loc, () => new[] { "chrome" });
        Sessions.Add(page, session);
        return page;
    }

    private static void ShootPage(Func<GroupPageViewModel> vm, string name)
    {
        var errors = BindingErrors.Capture(() => SnapshotRenderer.Capture(() => new Grid
        {
            Width = Canvas.Width,
            Height = Canvas.Height,
            Children =
            {
                new Atmosphere(new Brisk.Ui.AmbientClock(() => false)) { Decorated = false },
                new GroupPage { DataContext = vm() },
            },
        }, Canvas, name));
        Assert.Empty(errors);
    }

    /// Yer aç as the shell builds it, entered, at `level`; `run` presses the button and waits for the end.
    internal static SpacePageViewModel SpacePageOf(FakeBackend b, SpaceLevel level = SpaceLevel.Mid, bool run = false,
        string language = "tr")
    {
        var (home, session) = Home(b, language);
        var page = new SpacePageViewModel(session, home.L, () => new[] { "chrome" }, b.SystemDriveFreeBytes,
            a => a(), a => a());
        page.Enter();
        session.SetLevel(level);
        if (run) session.RunAsync(ActionGroupKind.Space).GetAwaiter().GetResult();
        return page;
    }

    private static Grid SpaceFrame(SpacePageViewModel vm) => new()
    {
        Width = Canvas.Width,
        Height = Canvas.Height,
        Children =
        {
            new Atmosphere(new Brisk.Ui.AmbientClock(() => false)) { Decorated = false },
            new SpacePage { DataContext = vm },
        },
    };

    private static void ShootSpace(Func<SpacePageViewModel> vm, string name, Action<FrameworkElement>? inspect = null)
    {
        var errors = BindingErrors.Capture(() =>
            SnapshotRenderer.Capture(() => SpaceFrame(vm()), Canvas, name, inspect));
        Assert.Empty(errors);
    }

    [Fact]
    public void Page_Space_WithDownloadsOpen() => ShootSpace(() =>
    {
        var p = SpacePageOf(Catalog());
        p.Expand("space.downloads");
        return p;
    }, "new-page-space");

    [Fact]
    public void Page_Space_Done() => ShootSpace(() => SpacePageOf(Catalog(), run: true), "new-page-space-done");

    /// The level colour is one brush the code-behind repaints: it must not be
    /// frozen once the page is on screen, or the cross-fade (moving
    /// background on) and the instant repaint (off) throw.
    [Fact]
    public void Page_Space_Max_LevelColourRepaintsOnScreen() => ShootSpace(() => SpacePageOf(Catalog()),
        "new-page-space-max", element =>
        {
            var page = (SpacePage)((Grid)element).Children[1];
            var vm = (SpacePageViewModel)page.DataContext;
            var brush = (System.Windows.Media.SolidColorBrush)page.Resources["LevelBrush"];

            Assert.False(brush.IsFrozen);
            var moving = Brisk.Ui.AmbientClock.ForStage(() => true, renderTier: () => 2);
            Brisk.Ui.AmbientClock.Install(moving);
            try
            {
                moving.SetVisible(true);
                vm.Levels[2].Select.Execute(null);
                Assert.True(brush.HasAnimatedProperties);
            }
            finally
            {
                Brisk.Ui.AmbientClock.Install(new Brisk.Ui.AmbientClock(() => true));
            }
            vm.Levels[0].Select.Execute(null);
            vm.Levels[2].Select.Execute(null);
            Assert.False(brush.HasAnimatedProperties);
            Assert.Equal(System.Windows.Media.ColorConverter.ConvertFromString(vm.LevelColor), brush.Color);
            element.UpdateLayout();
        });

    /// The 0.11.0 live panels are not on the pages (ruling R36).
    [Fact]
    public void Page_Speed() => ShootPage(() => Page(Catalog(), ActionGroupKind.Speed), "new-page-speed");

    [Fact]
    public void Page_Privacy() => ShootPage(() => Page(Catalog(), ActionGroupKind.Privacy), "new-page-privacy");

    internal static Brisk.Ui.Shell.ShellViewModel Shell(FakeBackend b, string language = "tr", Brisk.Ui.AmbientClock? clock = null)
    {
        var loc = new Loc();
        loc.SetLanguage(language);
        var gate = new object();
        Action<Action> post = a => { lock (gate) a(); };
        var session = new ActionSession(b, post);
        var settings = new Brisk.Ui.Shell.UiSettingsViewModel(new Brisk.Services.Settings { Language = language },
            Path.Combine(Path.GetTempPath(), "brisk-snap-settings.json"), loc, () => false, _ => true, () => { });
        var shell = new Brisk.Ui.Shell.ShellViewModel(post, a => a(), session, b, new ManualTimers(), loc,
            clock ?? new Brisk.Ui.AmbientClock(() => false), settings);
        session.LookAsync().GetAwaiter().GetResult();
        shell.SetWindowVisible(true);
        return shell;
    }

    private static void ShootShell(Func<Brisk.Ui.Shell.ShellViewModel> vm, string name)
    {
        var errors = BindingErrors.Capture(() =>
            SnapshotRenderer.Capture(() => new Brisk.Ui.Shell.ShellWindow(vm()), Canvas, name));
        Assert.Empty(errors);
    }

    [Fact]
    public void Shell_Home() => ShootShell(() => Shell(Catalog()), "new-shell-home");

    [Fact]
    public void Shell_Settings() => ShootShell(() =>
    {
        var s = Shell(Catalog());
        s.Current = Brisk.Ui.Shell.Place.Settings;
        return s;
    }, "new-shell-settings");

    [Fact]
    public void Shell_SpacePage_English() => ShootShell(() =>
    {
        var s = Shell(Catalog(), "en");
        s.Current = Brisk.Ui.Shell.Place.Space;
        return s;
    }, "new-shell-space-en");

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GroupPageViewModel, ActionSession> Sessions = new();
    private static ActionSession SessionOf(GroupPageViewModel page) => Sessions.TryGetValue(page, out var s) ? s : throw new InvalidOperationException();

    [Fact]
    public void Page_Privacy_AfterARun() => ShootPage(() =>
    {
        var b = Catalog();
        b.Outcome = i => i.Id == "privacy.copilot"
            ? new RowResult(i.Id, RowOutcome.Done, null, BriskEngine.Diagnostics.ReadBackState.Reverted, 0, 0)
            : new RowResult(i.Id, RowOutcome.Done, null, null, 0, 0);
        return Page(b, ActionGroupKind.Privacy, run: true);
    }, "new-page-privacy-done");

    internal static (HomeViewModel Vm, ActionSession Session) Home(FakeBackend b, string language = "tr")
    {
        var loc = new Loc();
        loc.SetLanguage(language);
        var gate = new object();
        Action<Action> post = a => { lock (gate) a(); };
        var session = new ActionSession(b, post);
        var vm = new HomeViewModel(post, a => a(), session, b, new ManualTimers(), loc, _ => { });
        session.LookAsync().GetAwaiter().GetResult();
        vm.Activate();
        return (vm, session);
    }

    internal static HomeViewModel AfterRun(FakeBackend b)
    {
        var (vm, session) = Home(b);
        session.RunAllAsync().GetAwaiter().GetResult();
        return vm;
    }

    private static FrameworkElement Frame(object vm) => new Grid
    {
        Width = Canvas.Width,
        Height = Canvas.Height,
        Children = { new Atmosphere(new Brisk.Ui.AmbientClock(() => false)), new HomeView { DataContext = vm } },
    };

    private static void Shoot(Func<object> vm, string name)
    {
        var errors = BindingErrors.Capture(() =>
            SnapshotRenderer.Capture(() => Frame(vm()), Canvas, name));
        Assert.Empty(errors);
        Assert.True(SnapshotRenderer.DistinctColors(Path.Combine(SnapshotDirFor(name))) > 50);
    }

    private static string SnapshotDirFor(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, ".snapshots", name + ".png");
        throw new InvalidOperationException();
    }

    [Fact]
    public void Atmosphere_Alone_ShowsRainAndTheGridFloor()
    {
        var path = SnapshotRenderer.Capture(() => new Atmosphere(new Brisk.Ui.AmbientClock(() => false)),
            Canvas, "new-atmosphere");
        // Grid lines are accent ink on the floor; the sky above has only rain.
        static bool Line(System.Windows.Media.Color c) => c.G > 70 && c.B > 80;
        var floor = SnapshotRenderer.InkInBand(path, new System.Windows.Point(550, 680), 0, 160, Line);
        var sky = SnapshotRenderer.InkInBand(path, new System.Windows.Point(550, 160), 0, 160, Line);
        Assert.True(floor > 3 * sky + 500, $"grid floor ink: floor {floor}, sky {sky}");
    }

    [Fact]
    public void Home_Ready() => Shoot(() => Home(Rich()).Vm, "new-home-ready");

    [Fact]
    public void Home_Ready_English() => Shoot(() => Home(Rich(), "en").Vm, "new-home-ready-en");

    /// A satellite brought to the front: the floor and the rings have turned,
    /// and the gauge, the lit spec block and the top three are the card's.
    [Fact]
    public void Home_GraphicsInFront() => Shoot(() =>
    {
        var (vm, _) = Home(Rich());
        vm.SelectPart(Part.Gpu);
        return vm;
    }, "new-home-gpu");

    [Fact]
    public void Home_Running()
    {
        var b = Rich();
        b.HoldRun = new ManualResetEventSlim(false);
        try
        {
            Shoot(() =>
            {
                var (vm, session) = Home(b);
                _ = session.RunAllAsync();
                SpinWait.SpinUntil(() => session.State == SessionState.Running, 2000);
                return vm;
            }, "new-home-running");
        }
        finally { b.HoldRun.Set(); }
    }

    private static readonly string[] Satellites = { "CpuSatellite", "GpuSatellite", "RamSatellite" };

    private static Rect BoundsIn(FrameworkElement view, System.Windows.Media.Visual ancestor, string name)
    {
        var element = (FrameworkElement)view.FindName(name)!;
        return element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));
    }

    /// The GIF moment's longest strip (freed, apps, settings, failed, the
    /// restart, the caches left for their apps, Geri al, Tekrar tara) stays
    /// on one line, right of the nav and clear of the floor satellites.
    [Fact]
    public void Home_Done_TheLongestStrip_StaysClearOfTheSatellites()
    {
        var b = Catalog();
        b.Outcome = i => i.Id switch
        {
            "speed.animations" => new RowResult(i.Id, RowOutcome.PendingRestart, null, null, 0, 0),
            "privacy.location" or "privacy.ads" => new RowResult(i.Id, RowOutcome.Failed, "x", null, 0, 0),
            _ => new RowResult(i.Id, RowOutcome.Done, null, null, i.Bytes ?? 0, 0),
        };
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 28_346_784_153, startupAfter: 3, privacyOff: 8)
            with { DeferredBytes = 2_300_000_000 };
        b.DeferredAppNames.AddRange(new[] { "chrome", "discord", "spotify" });
        var overlaps = new List<string>();
        Rect strip = default;
        SnapshotRenderer.Capture(() => Frame(AfterRun(b)), Canvas, "new-home-done-long", inspect: root =>
        {
            var view = (HomeView)((Grid)root).Children[1];
            strip = BoundsIn(view, view, "StripPanel");
            foreach (var s in Satellites)
                if (BoundsIn(view, view, s) is var r && r.IntersectsWith(strip)) overlaps.Add($"{s} {r}");
        });
        Assert.True(strip.Height < 60, $"the strip wrapped: {strip}");
        Assert.True(strip.Left >= 110, $"the strip runs under the nav: {strip}");
        Assert.Empty(overlaps);
    }

    /// Spec: the satellites never overlap the side columns at any supported
    /// window size (the mockup's right list hit the "Ekran kartı" label at
    /// 1400×900). The window at 1400×900, at the size it opens with on a
    /// 1080p laptop at 150 % (FirstWindowSize), and at its minimum; each part
    /// brought to the front, and the floor caught every 10° of a turn.
    [Theory]
    [InlineData(1400, 900, "1400x900")]
    [InlineData(0, 0, "first-size-1080p-150")]
    [InlineData(720, 460, "minimum")]
    public void Home_Satellites_StayClearOfTheSideColumns(double width, double height, string label)
    {
        if (width == 0)
        {
            var first = Brisk.Ui.Shell.FirstWindowSize.For(Canvas, new Thickness(8, 31, 8, 8), new Rect(0, 0, 1280, 672));
            (width, height) = (first.Width, first.Height);
        }
        var overlaps = new List<string>();
        var checks = 0;
        var errors = BindingErrors.Capture(() => SnapshotRenderer.Capture(() =>
        {
            var shell = Shell(Catalog());
            return new Brisk.Ui.Shell.ShellWindow(shell) { Width = width, Height = height };
        }, new Size(width, height), "new-home-layout-" + label, inspect: root =>
        {
            var window = (Brisk.Ui.Shell.ShellWindow)root;
            var stage = (FrameworkElement)window.FindName("Root")!;
            var view = Find<HomeView>(window)!;
            var floor = (FloorCarousel)view.FindName("Floor")!;
            void Check(string when)
            {
                stage.UpdateLayout();
                var columns = new[] { "LeftColumn", "RightColumn" }.Select(c => (c, BoundsIn(view, stage, c))).ToList();
                foreach (var s in Satellites)
                {
                    var r = BoundsIn(view, stage, s);
                    foreach (var (c, cr) in columns)
                        if (r.IntersectsWith(cr)) overlaps.Add($"{when}: {s} {r} overlaps {c} {cr}");
                }
                checks++;
            }
            foreach (var part in new[] { Part.Gpu, Part.Ram, Part.Cpu })
            {
                window.ViewModel.Home.SelectPart(part);
                Brisk.Views.OffscreenLayout.Settle(window);
                Check(part.ToString());
            }
            for (var angle = 0; angle < 360; angle += 10)
            {
                floor.BeginAnimation(FloorCarousel.ShownAngleProperty, null);
                floor.ShownAngle = angle;
                Check($"{angle}°");
            }
            Assert.True(stage.ActualWidth > 0 && stage.ActualHeight > 0, $"the stage did not lay out at {label}");
        }));
        Assert.Empty(errors);
        Assert.Equal(39, checks);
        Assert.Empty(overlaps);
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    [Fact]
    public void Home_Done()
    {
        var b = Rich();
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 28_346_784_153, startupAfter: 3, privacyOff: 1);
        Shoot(() => AfterRun(b), "new-home-done");
    }
}
