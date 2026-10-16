using System.IO;
using BriskEngine.Actions;
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

    private async Task<Kit> Make()
    {
        var k = new Kit();
        k.Loc.SetLanguage("tr");
        k.Clock = new AmbientClock(() => k.Settings.Motion);
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        var settings = new UiSettingsViewModel(k.Settings, Path.Combine(_dir, "settings.json"), k.Loc,
            () => k.AutostartOn, on => { if (k.AutostartWorks) k.AutostartOn = on; return k.AutostartWorks; },
            () => { k.MotionChanges++; k.Clock.Reevaluate(); });
        k.Shell = new ShellViewModel(post, a => a(), k.Session, k.Backend, k.Timers, k.Loc, k.Clock, settings);
        await k.Session.LookAsync();
        return k;
    }

    [Fact]
    public async Task Navigating_ActivatesOnlyTheShownPlace()
    {
        var k = await Make();
        k.Shell.SetWindowVisible(true);
        Assert.Equal(Place.Home, k.Shell.Current);
        var homeSamples = k.Backend.LoadSamples;
        Assert.True(homeSamples >= 1);

        k.Shell.Current = Place.Speed;
        Assert.Equal(homeSamples + 1, k.Backend.LoadSamples);      // the speed panel samples at once
        Assert.False(k.Shell.Home.IsActive);
        Assert.False(k.Shell.Decorated);

        k.Shell.Current = Place.Privacy;
        Assert.Equal(1, k.Backend.UsageReads);
        var reads = k.Backend.ConnectionReads;
        k.Timers.Advance(TimeSpan.FromSeconds(4));
        Assert.True(k.Backend.ConnectionReads > reads);

        k.Shell.Current = Place.Home;
        Assert.True(k.Shell.Decorated);
        Assert.True(k.Shell.Home.IsActive);
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

    [Fact]
    public async Task Sheet_Lines_SayWhatGoesForGood()
    {
        var k = await Make();
        k.Shell.Home.Primary.Execute(null);
        var sheet = k.Shell.Sheet!;
        Assert.Equal("Bunlar kalıcı silinecek", sheet.Title);
        Assert.Equal(new[] { "İndirilenler'deki 2 kurulum dosyası · <1 MB", "Geri Dönüşüm Kutusu · 7,5 GB" }, sheet.Lines);
    }

    [Fact]
    public async Task Sheet_Go_RunsEverything()
    {
        var k = await Make();
        k.Shell.Home.Primary.Execute(null);
        k.Shell.Sheet!.Go.Execute(null);
        Assert.Null(k.Shell.Sheet);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Contains("space.recycle-bin", k.Backend.Runs.Single().RowIds);
    }

    [Fact]
    public async Task Sheet_Skip_RunsWithoutTheUnrecoverableRows()
    {
        var k = await Make();
        k.Shell.Home.Primary.Execute(null);
        k.Shell.Sheet!.Skip.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.DoesNotContain("space.recycle-bin", k.Backend.Runs.Single().RowIds);
        Assert.Contains("space.caches", k.Backend.Runs.Single().RowIds);
    }

    [Fact]
    public async Task Sheet_SeeList_RunsNothing_AndOpensTheDownloads()
    {
        var k = await Make();
        k.Shell.Home.Primary.Execute(null);
        k.Shell.Sheet!.SeeList.Execute(null);
        Assert.Null(k.Shell.Sheet);
        Assert.Empty(k.Backend.Runs);
        Assert.Equal(Place.Space, k.Shell.Current);
        Assert.True(k.Shell.Space.Rows.Single(r => r.Id == "space.downloads").IsExpanded);
    }

    [Fact]
    public async Task Sheet_FromAPage_RunsOnlyThatGroup()
    {
        var k = await Make();
        k.Shell.Current = Place.Space;
        k.Shell.Space.RunGroup.Execute(null);
        k.Shell.Sheet!.Go.Execute(null);
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
