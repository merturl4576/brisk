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
        b.Load.AddRange(new[]
        {
            new AppLoad("chrome", "Google Chrome", 34, 2L << 30, false, null, null, new[] { 1 }, true),
            new AppLoad("claude", "Claude", 2, 1_500L << 20, false, null, null, new[] { 2 }, true),
            new AppLoad("discord", "Discord", 1, 900L << 20, false, null, null, new[] { 3 }, true),
        });
        b.Connections = new ConnectionSnapshot(new[]
        {
            new ConnectionRow("Google Chrome", "Google", "www.google.com", 6, false, false),
            new ConnectionRow("Discord", "Cloudflare", null, 2, false, false),
            new ConnectionRow("Windows", "Microsoft", "v10.events.data.microsoft.com", 1, true, false),
        }, true);
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
        if (run) session.RunAsync(null, skipUnrecoverable: true).GetAwaiter().GetResult();
        var loc = home.L;
        var page = new GroupPageViewModel(session, group, loc, (_, _) => { }, () => new[] { "chrome" });
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

    [Fact]
    public void Page_Space_WithDownloadsOpen() => ShootPage(() =>
    {
        var p = Page(Catalog(), ActionGroupKind.Space);
        p.Expand("space.downloads");
        return p;
    }, "new-page-space");

    [Fact]
    public void Page_Speed_WithWhatIsSlowingYou() => ShootPage(() =>
    {
        var b = Catalog();
        b.Load.Add(new AppLoad("MsMpEng", "MsMpEng", 22, 300L << 20, true, "action.load.note.defender",
            "Defender is scanning", new[] { 9 }, false));
        b.Startup.Add(new BriskEngine.Diagnostics.StartupEntry("HKCU", "Discord", true, false, @"C:\D\Discord.exe"));
        var page = Page(b, ActionGroupKind.Speed);
        var loc = page.L;
        var gate = new object();
        var load = new Brisk.Ui.Live.LoadPanelViewModel(a => { lock (gate) a(); }, a => a(), b,
            SessionOf(page), new ManualTimers(), loc);
        load.Activate();
        page.Above = new object[] { load };
        return page;
    }, "new-page-speed");

    [Fact]
    public void Page_Privacy_WithConnectionsAndUsage() => ShootPage(() =>
    {
        var b = Catalog();
        b.Usage = new[]
        {
            new UsageRow("Google Chrome", 3L << 30, 41L << 30), new UsageRow("Windows (system)", 900L << 20, 12L << 30),
            new UsageRow("Discord", 400L << 20, 6L << 30),
        };
        var page = Page(b, ActionGroupKind.Privacy);
        var loc = page.L;
        var gate = new object();
        Action<Action> post = a => { lock (gate) a(); };
        var conn = new Brisk.Ui.Live.ConnectionsViewModel(post, a => a(), b, new ManualTimers(), loc);
        var usage = new Brisk.Ui.Live.UsageViewModel(post, a => a(), b, loc);
        conn.Activate();
        usage.Activate();
        page.Below = new object[] { conn, usage };
        return page;
    }, "new-page-privacy");

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
    public void Shell_HomeWithTheSheet() => ShootShell(() =>
    {
        var s = Shell(Catalog());
        s.Home.Primary.Execute(null);
        return s;
    }, "new-shell-sheet");

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
        var vm = new HomeViewModel(post, a => a(), session, b, new ManualTimers(), loc, (_, _) => { }, _ => { });
        session.LookAsync().GetAwaiter().GetResult();
        vm.Activate();
        return (vm, session);
    }

    internal static HomeViewModel AfterRun(FakeBackend b)
    {
        var (vm, session) = Home(b);
        session.RunAsync(null).GetAwaiter().GetResult();
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
                _ = session.RunAsync(null, skipUnrecoverable: true);
                SpinWait.SpinUntil(() => session.State == SessionState.Running, 2000);
                return vm;
            }, "new-home-running");
        }
        finally { b.HoldRun.Set(); }
    }

    /// The GIF moment's longest line (freed, startup, privacy, deferred,
    /// failed, restart) must not grow into the Speed satellite above it.
    [Fact]
    public void Home_Done_TheLongestStateline_StaysClearOfTheSpeedSatellite()
    {
        var b = Rich();
        b.Outcome = i => i.Id switch
        {
            "speed.animations" => new RowResult(i.Id, RowOutcome.PendingRestart, null, null, 0, 0),
            "privacy.location" => new RowResult(i.Id, RowOutcome.Failed, "x", null, 0, 0),
            _ => new RowResult(i.Id, RowOutcome.Done, null, null, i.Bytes ?? 0, 0),
        };
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 28_346_784_153, startupAfter: 3, privacyOff: 8)
            with { DeferredBytes = 2_300_000_000 };
        b.DeferredAppNames.AddRange(new[] { "chrome", "discord", "spotify" });
        Rect stateline = default, satellite = default;
        SnapshotRenderer.Capture(() => Frame(AfterRun(b)), Canvas, "new-home-done-long", inspect: root =>
        {
            var view = (HomeView)((Grid)root).Children[1];
            Rect Bounds(string name) => ((FrameworkElement)view.FindName(name)!)
                .TransformToAncestor(view).TransformBounds(new Rect(((FrameworkElement)view.FindName(name)!).RenderSize));
            stateline = Bounds("StatelinePanel");
            satellite = Bounds("SpeedSatellite");
        });
        Assert.True(stateline.Height > 40, $"the line did not wrap as long lines do: {stateline}");
        Assert.False(stateline.IntersectsWith(satellite), $"stateline {stateline} overlaps satellite {satellite}");
    }

    [Fact]
    public void Home_Done()
    {
        var b = Rich();
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 28_346_784_153, startupAfter: 3, privacyOff: 1);
        Shoot(() => AfterRun(b), "new-home-done");
    }
}
