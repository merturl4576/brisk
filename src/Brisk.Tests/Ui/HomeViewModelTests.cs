using BriskEngine.Actions;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Ui.Home;

namespace Brisk.Tests.Ui;

public sealed class HomeViewModelTests
{
    private static readonly object Gate = new();

    private sealed class Kit
    {
        public FakeBackend Backend = FakeBackend.Typical();
        public ManualTimers Timers = new();
        public Loc Loc = new();
        public ActionSession Session = null!;
        public HomeViewModel Vm = null!;
        public List<(IReadOnlyList<ActionItem> Items, ActionGroupKind? Group)> Confirms = new();
        public List<ActionGroupKind> Opened = new();
    }

    private static async Task<Kit> Make(Action<FakeBackend>? setup = null)
    {
        var k = new Kit();
        setup?.Invoke(k.Backend);
        k.Loc.SetLanguage("tr");
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        k.Vm = new HomeViewModel(post, a => a(), k.Session, k.Backend, k.Timers, k.Loc,
            (items, group) => k.Confirms.Add((items, group)), g => k.Opened.Add(g));
        await k.Session.LookAsync();
        return k;
    }

    private static AppLoad Load(string name, double cpu, long ram) =>
        new(name, name, cpu, ram, false, null, null, new[] { 1 }, true);

    [Fact]
    public async Task Figures_FollowTheSession()
    {
        var k = await Make();
        Assert.Equal("Hepsini yap", k.Vm.Figures.Button);
        Assert.Equal("14 → 13", k.Vm.Figures.Speed.Value);
    }

    [Fact]
    public async Task Primary_WithUnrecoverableRows_AsksTheShell_AndDoesNotRun()
    {
        var k = await Make();
        k.Vm.Primary.Execute(null);
        var asked = Assert.Single(k.Confirms);
        Assert.Equal(new[] { "space.downloads", "space.recycle-bin" }, asked.Items.Select(i => i.Id));
        Assert.Null(asked.Group);
        Assert.Empty(k.Backend.Runs);
    }

    [Fact]
    public async Task Primary_WithoutUnrecoverableRows_Runs()
    {
        var k = await Make();
        k.Session.SetTicked("space.downloads", false);
        k.Session.SetTicked("space.recycle-bin", false);
        k.Vm.Primary.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Single(k.Backend.Runs);
        Assert.Empty(k.Confirms);
    }

    [Fact]
    public async Task Load_ShowsTopThree_CpuWhenAboveThreshold()
    {
        var k = await Make(b => b.Load.AddRange(new[]
        {
            Load("Chrome", 34, 2L << 30), Load("Claude", 2, 1L << 30), Load("Discord", 1, 900L << 20), Load("Steam", 1, 600L << 20),
        }));
        k.Vm.Activate();
        Assert.Equal(3, k.Vm.Load.Count);
        Assert.Equal(new LoadLine("Chrome", "%34 CPU"), k.Vm.Load[0]);
        Assert.Equal(new LoadLine("Claude", "1,0 GB"), k.Vm.Load[1]);
        Assert.False(k.Vm.LoadEmpty);
    }

    [Fact]
    public async Task Load_Nothing_SaysNone()
    {
        var k = await Make();
        k.Vm.Activate();
        Assert.True(k.Vm.LoadEmpty);
        Assert.Empty(k.Vm.Load);
    }

    [Fact]
    public async Task Load_SampledEveryThirtySeconds_OnlyWhileActive()
    {
        var k = await Make();
        k.Vm.Activate();
        Assert.Equal(1, k.Backend.LoadSamples);
        k.Timers.Advance(TimeSpan.FromSeconds(61));
        Assert.Equal(3, k.Backend.LoadSamples);
        k.Vm.Deactivate();
        k.Timers.Advance(TimeSpan.FromSeconds(120));
        Assert.Equal(3, k.Backend.LoadSamples);
    }

    [Fact]
    public async Task OnlineApps_IsDistinctAppsInTheSnapshot()
    {
        var k = await Make(b => b.Connections = new ConnectionSnapshot(new[]
        {
            new ConnectionRow("Chrome", "Google", null, 4, false, false),
            new ConnectionRow("Chrome", "Cloudflare", null, 1, false, false),
            new ConnectionRow("Discord", "Discord", null, 2, false, false),
        }, true));
        k.Vm.Activate();
        Assert.Equal(2, k.Vm.OnlineApps);
        Assert.Equal("2", k.Vm.Figures.Online);
    }

    [Fact]
    public async Task ReceiptPolling_StopsOnceAfterRestartNumbersArrive()
    {
        var k = await Make();
        k.Session.SetTicked("space.downloads", false);
        k.Session.SetTicked("space.recycle-bin", false);
        await k.Session.RunAsync(null);
        k.Vm.Activate();
        var before = k.Backend.ReceiptRefreshes;
        k.Timers.Advance(TimeSpan.FromSeconds(31));
        Assert.True(SpinWait.SpinUntil(() => k.Backend.ReceiptRefreshes > before, 3000));

        k.Backend.ReceiptToReturn = TestReceipts.Freed("run-1", 1, bootBefore: 40_000, bootAfter: 20_000) with
        {
            StartupRamConfirmed = true, TelemetryAfter = 0,
        };
        await k.Session.RefreshReceiptAsync();
        var settled = k.Backend.ReceiptRefreshes;
        k.Timers.Advance(TimeSpan.FromSeconds(120));
        Thread.Sleep(50);
        Assert.Equal(settled, k.Backend.ReceiptRefreshes);
    }

    [Fact]
    public async Task OpenFailed_OpensTheFirstGroupWithAFailedRow()
    {
        var k = await Make(b => b.Outcome = i => i.Id == "speed.animations"
            ? new RowResult(i.Id, RowOutcome.Failed, "x", null, 0, 0)
            : new RowResult(i.Id, RowOutcome.Done, null, null, 0, 0));
        await k.Session.RunAsync(null, skipUnrecoverable: true);
        k.Vm.OpenFailed.Execute(null);
        Assert.Equal(new[] { ActionGroupKind.Speed }, k.Opened);
    }

    [Fact]
    public async Task LanguageChange_RecomputesTheFigures()
    {
        var k = await Make();
        var raised = new List<string?>();
        k.Vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        k.Loc.SetLanguage("en");
        Assert.Equal("Do it all", k.Vm.Figures.Button);
        Assert.Contains(nameof(HomeViewModel.Figures), raised);
    }
}
