using System.IO;
using BriskEngine.Actions;
using BriskEngine.Cleaning;
using Brisk.Localization;
using Brisk.Services;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Ui;
using Brisk.Ui.Shell;

namespace Brisk.Tests.Ui;

public sealed class ShellViewModelTests : IDisposable
{
    private static readonly object Gate = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("brisk-shell-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private sealed class Kit
    {
        public FakeBackend Backend = FakeBackend.Typical();
        public ManualTimers Timers = new();
        public Loc Loc = new();
        public Settings Settings = new();
        public bool AutostartOn;
        public bool AutostartWorks = true;
        public int MotionChanges;
        public AmbientClock Clock = null!;
        public ActionSession Session = null!;
        public ShellViewModel Shell = null!;
    }

    /// look: false leaves the shell as the window opens, before the first look.
    private async Task<Kit> Make(bool look = true, Action<FakeBackend>? setup = null)
    {
        var k = new Kit();
        setup?.Invoke(k.Backend);
        k.Loc.SetLanguage("tr");
        k.Clock = new AmbientClock(() => k.Settings.Motion);
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        var settings = new UiSettingsViewModel(k.Settings, Path.Combine(_dir, "settings.json"), k.Loc,
            () => k.AutostartOn, on => { if (k.AutostartWorks) k.AutostartOn = on; return k.AutostartWorks; },
            () => { k.MotionChanges++; k.Clock.Reevaluate(); });
        k.Shell = new ShellViewModel(post, a => a(), k.Session, k.Backend, k.Timers, k.Loc, k.Clock, settings);
        if (look) await k.Session.LookAsync();
        return k;
    }

    private static readonly Place[] Tiles = { Place.Home, Place.Space, Place.Speed, Place.Privacy };

    /// First launch: the window opens on Genel with the live monitor, no
    /// welcome screen; the look has not delivered, so the number badges spin.
    [Fact]
    public async Task Starts_on_home()
    {
        var k = await Make(look: false);
        Assert.Equal(Place.Home, k.Shell.Current);
        Assert.Same(k.Shell.Home, k.Shell.CurrentContent);
        Assert.True(k.Shell.IsHome);
        Assert.True(k.Shell.Decorated);
        k.Shell.SetWindowVisible(true);
        Assert.True(k.Shell.Home.IsActive);
        Assert.Equal(new NavBadge(null, false, false), k.Shell.Badge(Place.Home));
        Assert.All(new[] { Place.Space, Place.Speed, Place.Privacy }, p =>
            Assert.Equal(new NavBadge(null, true, false), k.Shell.Badge(p)));
    }

    /// Speed and Privacy come with the fast half of the look, Yer aç with the
    /// space scan; each badge spins until its own numbers land.
    [Fact]
    public async Task Badges_spin_until_scan_arrives()
    {
        var hold = new ManualResetEventSlim(false);
        var k = await Make(look: false, setup: b => b.HoldScan = hold);
        var raised = new List<string>();
        k.Shell.PropertyChanged += (_, e) => { lock (raised) raised.Add(e.PropertyName!); };

        var look = k.Session.LookAsync();
        // The bound property: it is refreshed after the session's change lands.
        Assert.True(SpinWait.SpinUntil(() => !k.Shell.SpeedBadge.Spinning, 3000));
        // Spotify and animations; ads. OneDrive and location are off by default.
        Assert.Equal(new NavBadge("2", false, false), k.Shell.Badge(Place.Speed));
        Assert.Equal(new NavBadge("1", false, false), k.Shell.Badge(Place.Privacy));
        Assert.Equal(new NavBadge(null, true, false), k.Shell.Badge(Place.Space));
        Assert.Equal(k.Shell.Badge(Place.Speed), k.Shell.SpeedBadge);

        hold.Set();
        await look;
        Assert.Equal(new NavBadge("5,6 GB", false, false), k.Shell.Badge(Place.Space));
        Assert.Equal(k.Shell.Badge(Place.Space), k.Shell.SpaceBadge);
        lock (raised)
        {
            Assert.Contains(nameof(ShellViewModel.SpeedBadge), raised);
            Assert.Contains(nameof(ShellViewModel.SpaceBadge), raised);
        }
        Assert.Equal(new NavBadge(null, false, false), k.Shell.Badge(Place.Home));
    }

    /// Yer aç shows the selected level's known bytes minus skips: a row with
    /// no size adds nothing, personal files never count, 0 hides the number.
    [Fact]
    public async Task Space_badge_follows_level()
    {
        var k = await Make(setup: b =>
        {
            var rows = b.Space!.Items.Append(FakeBackend.Row("space.unsized", ActionGroupKind.Space, null)).ToList();
            b.Space = b.Space with { Items = rows };
            b.Personal.Add(new PersonalFile("kali.vhdx", @"C:\VMs\kali.vhdx", 60L << 30));
        });
        Assert.True(SpinWait.SpinUntil(() => k.Session.PersonalFilesReady, 3000));
        // MIN: the caches alone, 6 000 000 000 bytes.
        Assert.Equal("5,6 GB", k.Shell.Badge(Place.Space).Text);

        var raised = new List<string>();
        k.Shell.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        k.Session.SetLevel(SpaceLevel.Mid);
        // + shaders, the two downloads and the recycle bin.
        Assert.Equal("15 GB", k.Shell.Badge(Place.Space).Text);
        Assert.Equal("15 GB", k.Shell.SpaceBadge.Text);
        Assert.Contains(nameof(ShellViewModel.SpaceBadge), raised);
        Assert.Equal(16_000_000_300, k.Session.LevelBytes(SpaceLevel.Mid));

        k.Session.ToggleSkip("space.recycle-bin");
        Assert.Equal("7,5 GB", k.Shell.Badge(Place.Space).Text);

        k.Loc.SetLanguage("en");
        Assert.Equal("7.5 GB", k.Shell.SpaceBadge.Text);

        k.Session.SetLevel(SpaceLevel.Min);
        k.Session.ToggleSkip("space.caches");
        Assert.Equal(new NavBadge(null, false, false), k.Shell.Badge(Place.Space));
        Assert.Equal(new NavBadge(null, false, false), k.Shell.SpaceBadge);

        // Speed and Privacy count the rows that are not skipped.
        k.Session.ToggleSkip("privacy.ads");
        Assert.Equal(new NavBadge(null, false, false), k.Shell.Badge(Place.Privacy));
        k.Session.ToggleSkip("speed.animations");
        Assert.Equal("1", k.Shell.Badge(Place.Speed).Text);
    }

    /// ✓ after that page's own run; Hepsini yap ticks every tile; Geri al and
    /// a new look take the ticks away.
    [Fact]
    public async Task Badge_done_after_page_run()
    {
        var k = await Make();
        k.Shell.Speed.RunGroup.Execute(null);
        // The bound property: the tick lands with the session's change, a
        // moment after State reads Done on another thread.
        Assert.True(SpinWait.SpinUntil(() => k.Shell.SpeedBadge.Done, 3000));
        Assert.Equal(SessionState.Done, k.Session.State);
        Assert.True(k.Shell.Badge(Place.Speed).Done);
        Assert.Equal("2", k.Shell.Badge(Place.Speed).Text);
        Assert.All(new[] { Place.Home, Place.Space, Place.Privacy }, p => Assert.False(k.Shell.Badge(p).Done));

        // Geri al reverses the speed run: its tick goes.
        await k.Session.UndoAsync();
        Assert.Contains("startup:HKCU:Spotify", k.Backend.UndoneRules);
        Assert.All(Tiles, p => Assert.False(k.Shell.Badge(p).Done));

        k.Shell.Space.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Shell.SpaceBadge.Done, 3000));
        Assert.Equal(SessionState.Done, k.Session.State);
        Assert.True(k.Shell.Badge(Place.Space).Done);
        Assert.False(k.Shell.Badge(Place.Speed).Done);

        // A new look delivers: every tick goes.
        await k.Session.RescanAsync();
        Assert.All(Tiles, p => Assert.False(k.Shell.Badge(p).Done));

        // Hepsini yap ticks Genel and the three pages.
        await k.Session.RunAllAsync();
        Assert.Equal(SessionState.Done, k.Session.State);
        Assert.All(Tiles, p => Assert.True(k.Shell.Badge(p).Done));
        Assert.True(k.Shell.HomeBadge.Done);
        Assert.False(k.Shell.Badge(Place.Settings).Done);

        // Geri al after Hepsini yap: Genel, Hızlandır and Gizlilik lose theirs.
        await k.Session.UndoAsync();
        Assert.All(Tiles, p => Assert.False(k.Shell.Badge(p).Done));
    }

    /// Only the place on screen runs: Home's monitor, or the page's scan.
    /// The pages stand on the stage too; the 0.11.0 live panels are not on
    /// them (ruling R36), so nothing samples load or connections.
    [Fact]
    public async Task Navigating_ActivatesOnlyTheShownPlace()
    {
        var k = await Make();
        k.Shell.SetWindowVisible(true);
        Assert.Equal(Place.Home, k.Shell.Current);
        var homeSamples = k.Backend.MonitorSamples;                 // Home's live monitor
        Assert.True(homeSamples >= 1);

        k.Shell.Current = Place.Speed;
        Assert.False(k.Shell.Home.IsActive);
        Assert.True(k.Shell.Speed.IsActive);
        Assert.True(k.Shell.Decorated);
        // Entering plays the page's scan; the moving background is on.
        Assert.Equal(Brisk.Ui.Pages.PagePhase.Scanning, k.Shell.Speed.Phase);

        k.Shell.Current = Place.Privacy;
        Assert.False(k.Shell.Speed.IsActive);
        Assert.True(k.Shell.Privacy.IsActive);
        k.Timers.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(Brisk.Ui.Pages.PagePhase.Ready, k.Shell.Privacy.Phase);
        Assert.Equal(homeSamples, k.Backend.MonitorSamples);       // Home's sampler stopped with it

        k.Shell.Current = Place.Home;
        Assert.True(k.Shell.Decorated);
        Assert.True(k.Shell.Home.IsActive);
        Assert.False(k.Shell.Privacy.IsActive);
    }

    /// The nav badge and the page's button share one count (ruling R35): a
    /// row added on a page counts on its tile too.
    [Fact]
    public async Task Badge_count_is_the_page_buttons_count()
    {
        var k = await Make();
        Assert.Equal("2", k.Shell.SpeedBadge.Text);
        Assert.Equal("2 değişikliği yap", k.Shell.Speed.Button);
        k.Shell.Speed.Extra.Single().Tap();                          // OneDrive, off by default
        Assert.Equal(3, k.Shell.Speed.SelectedCount);
        Assert.Equal("3", k.Shell.SpeedBadge.Text);
        Assert.Equal("3 değişikliği yap", k.Shell.Speed.Button);   // two apps and a setting
        k.Shell.Privacy.Main.Single().Tap();
        Assert.Null(k.Shell.PrivacyBadge.Text);
        Assert.Equal("Hiçbir şey seçili değil", k.Shell.Privacy.Button);
    }

    /// ✓ follows the page's done state: a run where every row failed leaves
    /// no tick (ruling R29); one row through is enough.
    [Fact]
    public async Task Badge_done_only_when_a_row_went_through()
    {
        var k = await Make(setup: b => b.Outcome = i => new RowResult(i.Id, RowOutcome.Failed, "denied", null, 0, 0));
        await k.Session.RunAsync(ActionGroupKind.Privacy);
        Assert.Equal(SessionState.Done, k.Session.State);
        Assert.False(k.Shell.PrivacyBadge.Done);

        // Hepsini yap: only the Speed row went through.
        await k.Session.RescanAsync();
        k.Backend.Outcome = i => i.Id == "speed.animations"
            ? new RowResult(i.Id, RowOutcome.Done, null, null, 0, 0)
            : new RowResult(i.Id, RowOutcome.Failed, "denied", null, 0, 0);
        await k.Session.RunAllAsync();
        Assert.True(k.Shell.SpeedBadge.Done);
        Assert.True(k.Shell.HomeBadge.Done);
        Assert.False(k.Shell.PrivacyBadge.Done);
        Assert.False(k.Shell.SpaceBadge.Done);
    }

    [Fact]
    public async Task HiddenWindow_StopsLiveReadersAndMotion()
    {
        var k = await Make();
        k.Shell.SetWindowVisible(true);
        Assert.True(k.Clock.IsActive);
        k.Shell.SetWindowVisible(false);
        Assert.False(k.Clock.IsActive);
        Assert.False(k.Shell.Home.IsActive);
        k.Shell.SetWindowVisible(true);
        Assert.True(k.Shell.Home.IsActive);
    }

    /// No confirmation sheet: Hepsini yap runs everything at MIN at once and
    /// the window stays where it is; the MID recycle bin is not in it.
    [Fact]
    public async Task HomePrimary_RunsEverything_WithoutAConfirmation()
    {
        var k = await Make();
        k.Shell.Home.Primary.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        var run = k.Backend.Runs.Single().RowIds;
        Assert.Contains("space.caches", run);
        Assert.Contains("privacy.ads", run);
        Assert.DoesNotContain("space.recycle-bin", run);
        Assert.Equal(Place.Home, k.Shell.Current);
    }

    [Fact]
    public async Task APageRun_RunsOnlyThatGroup_WithoutAConfirmation()
    {
        var k = await Make();
        k.Shell.Current = Place.Space;
        k.Shell.Space.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.All(k.Backend.Runs.Single().RowIds, id => Assert.StartsWith("space.", id));
    }

    [Fact]
    public async Task Settings_LanguageSwitch_SavesAndRetitles()
    {
        var k = await Make();
        k.Shell.Settings.Language = "en";
        Assert.Equal("Free up space", k.Shell.Space.Title);
        Assert.Equal("en", Settings.Load(Path.Combine(_dir, "settings.json")).Language);
    }

    [Fact]
    public async Task Settings_MotionOff_StopsTheAmbientClock_AndSaves()
    {
        var k = await Make();
        k.Shell.SetWindowVisible(true);
        Assert.True(k.Clock.IsActive);
        k.Shell.Settings.Motion = false;
        Assert.False(k.Clock.IsActive);
        Assert.False(Settings.Load(Path.Combine(_dir, "settings.json")).Motion);
    }

    [Fact]
    public async Task Settings_AutostartRefused_KeepsTheSwitchOff()
    {
        var k = await Make();
        k.AutostartWorks = false;
        k.Shell.Settings.StartWithWindows = true;
        Assert.False(k.Shell.Settings.StartWithWindows);
        Assert.False(k.Settings.StartWithWindows);
    }

    [Fact]
    public async Task OpenFromHome_GoesToThePage()
    {
        var k = await Make();
        k.Shell.Open(ActionGroupKind.Privacy);
        Assert.Equal(Place.Privacy, k.Shell.Current);
        Assert.Same(k.Shell.Privacy, k.Shell.CurrentContent);
    }
}
