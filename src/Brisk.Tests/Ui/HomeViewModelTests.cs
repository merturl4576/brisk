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
    private static readonly StringComparer Ordinal = StringComparer.Ordinal;

    private sealed class Kit
    {
        public FakeBackend Backend = FakeBackend.Typical();
        public ManualTimers Timers = new();
        public Loc Loc = new();
        public ActionSession Session = null!;
        public HomeViewModel Vm = null!;
        public List<ActionGroupKind> Opened = new();
    }

    private static async Task<Kit> Make(Action<FakeBackend>? setup = null)
    {
        var k = new Kit();
        setup?.Invoke(k.Backend);
        k.Loc.SetLanguage("tr");
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        k.Vm = new HomeViewModel(post, a => a(), k.Session, k.Backend, k.Timers, k.Loc, g => k.Opened.Add(g));
        await k.Session.LookAsync();
        return k;
    }

    /// Waits for a session state as the window would see it: read under the
    /// post gate, so a change and the Changed it raises are seen together.
    private static bool Until(Func<bool> condition) =>
        SpinWait.SpinUntil(() => { lock (Gate) return condition(); }, 3000);

    private static MonitorSample Sample(double? cpu = 20, double? gpu = 12, double? ram = 58,
        double? cpuTemp = 66, double? gpuTemp = 52, BatteryInfo? battery = null) => new(
        cpu, gpu, ram, ram is null ? 0 : 18L << 30, cpuTemp, gpuTemp, 10_375, 14_000, battery,
        new[] { new ProcLoad("Claude Code", 9), new ProcLoad("Google Chrome", 6), new ProcLoad("Visual Studio Code", 4) },
        new[] { new ProcLoad("Google Chrome", 8), new ProcLoad("LivelyWallpaper", 6), new ProcLoad("Masaüstü (dwm)", 4) },
        new[] { new ProcLoad("Visual Studio Code", 6_013_000_000), new ProcLoad("Claude Code", 2_900_000_000), new ProcLoad("Wispr Flow", 634L << 20) });

    /// The hepsini-yap row that 0.11.0 recorded as undoable: a Space rule in
    /// the run that Geri al must leave alone (ruling R21).
    private static void WithHibernation(FakeBackend b)
    {
        var hibernation = ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.hibernation"), 1L << 30)
            with { Reversible = true, Level = SpaceLevel.Min };
        b.Space = new ActionGroup(ActionGroupKind.Space, b.Space!.Items.Append(hibernation).ToList());
    }

    // ---- the five from the brief

    /// "o kısma basınca ana ekran dönmesin": a right-column icon lights its
    /// spec block and nothing else.
    [Fact]
    public async Task Spec_icon_never_rotates_gauge()
    {
        var k = await Make(b => b.MonitorToReturn = Sample());
        k.Vm.Activate();
        var gauge = k.Vm.Gauge;
        var (floor, rings) = (k.Vm.FloorAngle, k.Vm.RingAngle);

        k.Vm.HighlightSpec(SpecBlock.Gpu);
        k.Vm.ShowDiskSpec.Execute(null);
        k.Vm.ShowRamSpec.Execute(null);

        Assert.Equal(SpecBlock.Ram, k.Vm.HighlightedSpec);
        Assert.Equal(Part.Cpu, k.Vm.Selected);
        Assert.Equal(floor, k.Vm.FloorAngle);
        Assert.Equal(rings, k.Vm.RingAngle);
        Assert.Equal(gauge, k.Vm.Gauge);
        Assert.Equal("İşlemci", k.Vm.Gauge.Label);
        Assert.Equal("Claude Code", k.Vm.Top[0].Name);
    }

    /// A floor satellite brings its part to the front: the floor turns the
    /// short way, the rings turn 120°, and the gauge, its spec block and the
    /// top three follow.
    [Fact]
    public async Task Satellite_rotates_gauge()
    {
        var k = await Make(b => b.MonitorToReturn = Sample());
        k.Vm.Activate();
        Assert.Equal(Part.Cpu, k.Vm.Selected);
        Assert.Equal("20", k.Vm.Gauge.Value);
        Assert.Equal(0.20, k.Vm.Gauge.Fraction, 3);

        k.Vm.SelectPart(Part.Gpu);
        Assert.Equal(Part.Gpu, k.Vm.Selected);
        Assert.Equal(SpecBlock.Gpu, k.Vm.HighlightedSpec);
        Assert.Equal(-120, k.Vm.FloorAngle);
        Assert.Equal(120, Math.Abs(k.Vm.RingAngle));
        Assert.Equal("Ekran kartı", k.Vm.Gauge.Label);
        Assert.Equal("12", k.Vm.Gauge.Value);
        Assert.Equal("52 °C", k.Vm.Gauge.Temperature);
        Assert.Equal(new InfoLine("Google Chrome", "8 %"), k.Vm.Top[0]);

        k.Vm.SelectRam.Execute(null);
        Assert.Equal(Part.Ram, k.Vm.Selected);
        Assert.Equal(-240, k.Vm.FloorAngle);
        Assert.Equal(240, Math.Abs(k.Vm.RingAngle));
        Assert.Equal("Bellek", k.Vm.Gauge.Label);
        Assert.Equal("58", k.Vm.Gauge.Value);
        Assert.Null(k.Vm.Gauge.Temperature);                 // memory has no sensor: the line hides
        Assert.Equal(new InfoLine("Visual Studio Code", "5,6 GB"), k.Vm.Top[0]);

        // Back to the front the short way: one more step, not two back.
        k.Vm.SelectPart(Part.Cpu);
        Assert.Equal(-360, k.Vm.FloorAngle);
        Assert.Equal(360, Math.Abs(k.Vm.RingAngle));

        // The part already in front turns nothing.
        k.Vm.SelectPart(Part.Cpu);
        Assert.Equal(-360, k.Vm.FloorAngle);
    }

    /// The mockup's motion: a pick counts the number and sweeps the arc to
    /// the new part; the reading every second after the turn lands at once.
    [Fact]
    public async Task Only_a_turn_glides()
    {
        var k = await Make(b => b.MonitorToReturn = Sample());
        k.Vm.Activate();
        Assert.False(k.Vm.Gliding);
        var order = new List<string?>();
        k.Vm.PropertyChanged += (_, e) => order.Add(e.PropertyName);

        k.Vm.SelectPart(Part.Gpu);
        Assert.True(k.Vm.Gliding);
        Assert.True(order.IndexOf(nameof(HomeViewModel.Gliding)) < order.IndexOf(nameof(HomeViewModel.Gauge)));

        k.Timers.Advance(HomeViewModel.TurnFor);
        Assert.False(k.Vm.Gliding);
        k.Vm.HighlightSpec(SpecBlock.Disk);
        Assert.False(k.Vm.Gliding);
    }

    /// A desktop: no battery, no Pil block. A battery that keeps its wear to
    /// itself shows charge and power only.
    [Fact]
    public async Task No_battery_hides_block()
    {
        var k = await Make(b => b.MonitorToReturn = Sample(battery: null));
        k.Vm.Activate();
        Assert.Null(k.Vm.Live.Battery);

        k.Backend.MonitorToReturn = Sample(battery: new BatteryInfo(80, false, null, null));
        k.Timers.Advance(HomeViewModel.SampleEvery);
        var battery = Assert.IsType<BatteryFigures>(k.Vm.Live.Battery);
        Assert.Equal("80 %", battery.Charge);
        Assert.Equal("Pilde", battery.State);
        Assert.Null(battery.Health);
        Assert.Null(battery.Cycles);

        k.Backend.MonitorToReturn = Sample(battery: new BatteryInfo(100, true, 29, 312));
        k.Timers.Advance(HomeViewModel.SampleEvery);
        battery = k.Vm.Live.Battery!;
        Assert.Equal("Prizde", battery.State);
        Assert.Equal("29 %", battery.Health);
        Assert.Equal("312", battery.Cycles);
    }

    /// Unreadable is "—", never a guess: temperatures, a load not primed yet,
    /// memory unread (RamUsedBytes is 0 then, and is not a reading).
    [Fact]
    public async Task Unread_temp_shows_dash()
    {
        var k = await Make(b => b.MonitorToReturn = new MonitorSample(null, null, null, 0, null, null, null, null,
            null, Array.Empty<ProcLoad>(), Array.Empty<ProcLoad>(), Array.Empty<ProcLoad>()));
        Assert.Equal("—", k.Vm.Gauge.Value);                 // the first frame, before any sample
        Assert.Equal("—", k.Vm.Live.CpuTemp);

        k.Vm.Activate();
        Assert.Equal("—", k.Vm.Live.CpuTemp);
        Assert.Equal("—", k.Vm.Live.GpuTemp);
        Assert.Equal("—", k.Vm.Gauge.Temperature);
        Assert.False(k.Vm.Gauge.Hot);
        Assert.Equal("—", k.Vm.Gauge.Value);
        Assert.Null(k.Vm.Gauge.Percent);
        Assert.Equal(0, k.Vm.Gauge.Fraction);
        Assert.Equal("—", k.Vm.Live.Upload);
        Assert.Equal("—", k.Vm.Live.CpuLoad);

        k.Vm.SelectPart(Part.Gpu);
        Assert.Equal("—", k.Vm.Gauge.Temperature);

        k.Vm.SelectPart(Part.Ram);
        Assert.Equal("—", k.Vm.Gauge.Value);
        Assert.Equal("—", k.Vm.Live.RamLoad);
        Assert.Null(k.Vm.Gauge.Caption);                     // not "0 GB in use"
        Assert.Equal(new[] { new InfoLine("—", "") }, k.Vm.Top);
    }

    /// Ruling R4 and R21: at rest the strip counts what Hepsini yap takes;
    /// after the run it offers Geri al, which reverses Speed and Privacy
    /// alone, and a run that changed no Speed or Privacy setting offers none.
    [Fact]
    public async Task Strip_after_run_all_offers_undo_only_for_speed_privacy()
    {
        var k = await Make(WithHibernation);
        Assert.Equal("brisk hazır: 6,6 GB · 2 uygulama · 1 ayar", k.Vm.Strip.Text);
        Assert.Equal("Hepsini yap", k.Vm.Strip.Button);
        Assert.False(k.Vm.Strip.ShowUndo);

        k.Vm.Primary.Execute(null);
        Assert.True(Until(() => k.Session.State == SessionState.Done));
        Assert.Equal("Son işlem: 6,6 GB · 2 uygulama · 1 ayar", k.Vm.Strip.Text);
        Assert.Null(k.Vm.Strip.Button);
        Assert.True(k.Vm.Strip.ShowUndo);
        Assert.Contains("hibernation", k.Session.LastRun!.UndoableRuleIds);

        k.Vm.Undo.Execute(null);
        Assert.True(Until(() => k.Session.UndoResults is not null && k.Session.State == SessionState.Ready));
        Assert.Equal(new[] { "privacy.ads", "speed.animations", "startup:HKCU:Spotify" },
            k.Backend.UndoneRules.OrderBy(i => i, Ordinal));
        Assert.False(k.Vm.Strip.ShowUndo);
        Assert.Contains("silinen dosyalar geri gelmez", k.Vm.Strip.Text);

        // Speed and Privacy all skipped: the run holds a Space rule alone.
        var space = await Make(WithHibernation);
        foreach (var id in new[] { "startup:HKCU:Spotify", "speed.animations", "privacy.ads" }) space.Session.ToggleSkip(id);
        Assert.Equal("brisk hazır: 6,6 GB · 0 uygulama · 0 ayar", space.Vm.Strip.Text);
        space.Vm.Primary.Execute(null);
        Assert.True(Until(() => space.Session.State == SessionState.Done));
        Assert.Equal(new[] { "hibernation" }, space.Session.LastRun!.UndoableRuleIds);
        Assert.Equal("Son işlem: 6,6 GB", space.Vm.Strip.Text);
        Assert.False(space.Vm.Strip.ShowUndo);
        Assert.False(space.Vm.Undo.CanExecute(null));
    }

    // ---- the strip and its button

    /// No confirmation: Hepsini yap runs at once — Yer aç at MIN and the
    /// default Speed and Privacy rows; the MID rows that cannot come back
    /// (Downloads, Recycle Bin) are the Yer aç page's.
    [Fact]
    public async Task Primary_RunsAll_WithoutAsking()
    {
        var k = await Make();
        Assert.Equal("brisk hazır: 5,6 GB · 2 uygulama · 1 ayar", k.Vm.Strip.Text);
        k.Vm.Primary.Execute(null);
        Assert.True(Until(() => k.Session.State == SessionState.Done));
        var ran = Assert.Single(k.Backend.Runs);
        Assert.Contains("space.caches", ran.RowIds);
        Assert.Contains("privacy.ads", ran.RowIds);
        Assert.DoesNotContain("space.downloads", ran.RowIds);
        Assert.DoesNotContain("space.recycle-bin", ran.RowIds);
        Assert.Null(k.Session.RunGroup);
    }

    /// The counts follow a tap on a page: a skipped row leaves the strip,
    /// as it leaves Hepsini yap.
    [Fact]
    public async Task Strip_follows_a_skip()
    {
        var k = await Make();
        k.Session.ToggleSkip("startup:HKCU:Spotify");
        k.Session.ToggleSkip("space.caches");
        Assert.Equal("brisk hazır: 0 GB · 1 uygulama · 1 ayar", k.Vm.Strip.Text);
    }

    [Fact]
    public async Task Strip_while_looking_waits_and_a_failed_look_offers_retry()
    {
        var k = new Kit();
        k.Backend.HoldScan = new ManualResetEventSlim(false);
        k.Loc.SetLanguage("tr");
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        k.Vm = new HomeViewModel(post, a => a(), k.Session, k.Backend, k.Timers, k.Loc, _ => { });
        var look = k.Session.LookAsync();
        Assert.True(Until(() => k.Session.State == SessionState.Looking));
        Assert.Equal("Hepsini yap", k.Vm.Strip.Button);
        Assert.False(k.Vm.Strip.ButtonEnabled);
        Assert.False(k.Vm.Primary.CanExecute(null));
        k.Backend.HoldScan.Set();
        await look;

        k.Backend.FailSpace = new InvalidOperationException("disk");
        await k.Session.RescanAsync();
        Assert.Equal(Tone.Bad, k.Vm.Strip.Tone);
        Assert.Equal("Tekrar dene", k.Vm.Strip.Button);
        k.Backend.FailSpace = null;
        k.Vm.Primary.Execute(null);
        Assert.True(Until(() => k.Session.State == SessionState.Ready));
    }

    // ---- sampling

    [Fact]
    public async Task Monitor_sampled_every_second_only_while_active()
    {
        var k = await Make();
        Assert.Equal(0, k.Backend.MonitorSamples);
        k.Vm.Activate();
        Assert.Equal(1, k.Backend.MonitorSamples);
        k.Timers.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(4, k.Backend.MonitorSamples);
        k.Vm.Deactivate();
        k.Timers.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(4, k.Backend.MonitorSamples);
    }

    /// The monitor and the specs are read off the UI thread; a tick while a
    /// sample is still out is skipped, never stacked; the specs are asked once.
    [Fact]
    public async Task Sampling_runs_in_the_background_and_never_overlaps()
    {
        var k = new Kit();
        k.Loc.SetLanguage("tr");
        var queued = new List<Action>();
        Action<Action> post = a => { lock (Gate) a(); };
        k.Session = new ActionSession(k.Backend, post);
        k.Backend.MonitorToReturn = Sample();
        k.Vm = new HomeViewModel(post, queued.Add, k.Session, k.Backend, k.Timers, k.Loc, _ => { });
        await k.Session.LookAsync();

        k.Vm.Activate();
        Assert.Equal(0, k.Backend.MonitorSamples);          // nothing read on the calling thread
        Assert.Equal(0, k.Backend.SpecsReads);
        Assert.Equal("—", k.Vm.Gauge.Value);
        Assert.Equal("—", k.Vm.Specs.Cpu);
        Assert.Equal(2, queued.Count);                       // one sample, one specs read

        k.Timers.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, queued.Count);                       // the sample is still out: ticks skipped

        foreach (var work in queued.ToList()) work();
        queued.Clear();
        Assert.Equal(1, k.Backend.MonitorSamples);
        Assert.Equal(1, k.Backend.SpecsReads);
        Assert.Equal("20", k.Vm.Gauge.Value);
        Assert.Equal("Test CPU · 2,60 GHz\n6 çekirdek, 12 iş parçacığı", k.Vm.Specs.Cpu);

        k.Timers.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(queued);                               // the next tick samples again; specs are kept
        k.Vm.Deactivate();
        k.Vm.Activate();
        Assert.Single(queued);                               // still out: coming back does not stack another
        foreach (var work in queued.ToList()) work();
        Assert.Equal(2, k.Backend.MonitorSamples);
        Assert.Equal(1, k.Backend.SpecsReads);
    }

    [Fact]
    public async Task Specs_show_what_was_read_and_dash_what_was_not()
    {
        var k = await Make(b => b.SpecsToReturn = new SpecsInfo("Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz", 2592, 6, 12,
            new[] { "Intel(R) UHD Graphics", "NVIDIA GeForce GTX 1650 Ti" }, 32L << 30, 34_118_000_000,
            "KINGSTON SKC3000S1024G", 1_024_209_543_168));
        k.Vm.Activate();
        Assert.Equal("Intel Core i7-10750H · 2,59 GHz\n6 çekirdek, 12 iş parçacığı", k.Vm.Specs.Cpu);
        Assert.Equal("Intel UHD Graphics\nNVIDIA GeForce GTX 1650 Ti", k.Vm.Specs.Gpu);
        Assert.Equal("32 GB (31,8 GB kullanılabilir)", k.Vm.Specs.Ram);
        Assert.Equal("KINGSTON SKC3000S1024G · 1 TB", k.Vm.Specs.Disk);

        var unread = await Make(b => b.SpecsToReturn = new SpecsInfo(null, null, null, null, Array.Empty<string>(),
            null, null, null, null));
        unread.Vm.Activate();
        Assert.Equal(new SpecFigures("—", "—", "—", "—"), unread.Vm.Specs);
    }

    [Fact]
    public async Task Left_column_reads_the_network_in_bits()
    {
        var k = await Make(b => b.MonitorToReturn = Sample());
        k.Vm.Activate();
        Assert.Equal("83 Kbps", k.Vm.Live.Upload);           // 10 375 B/s
        Assert.Equal("112 Kbps", k.Vm.Live.Download);        // 14 000 B/s
        Assert.Equal("66 °C", k.Vm.Live.CpuTemp);
        Assert.Equal("20 %", k.Vm.Live.CpuLoad);
        k.Vm.SelectPart(Part.Ram);
        Assert.Equal("18 GB / 31 GB kullanılıyor", k.Vm.Gauge.Caption);
    }

    // ---- kept from 0.11.0

    [Fact]
    public async Task ReceiptPolling_StopsOnceAfterRestartNumbersArrive()
    {
        var k = await Make();
        await k.Session.RunAllAsync();
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
        await k.Session.RunAllAsync();
        Assert.EndsWith("1 iş olmadı", k.Vm.Strip.Text);
        k.Vm.OpenFailed.Execute(null);
        Assert.Equal(new[] { ActionGroupKind.Speed }, k.Opened);
    }

    [Fact]
    public async Task LanguageChange_RecomputesTheStripAndTheGauge()
    {
        var k = await Make(b => b.MonitorToReturn = Sample());
        k.Vm.Activate();
        var raised = new List<string?>();
        k.Vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        k.Loc.SetLanguage("en");
        Assert.Equal("Do it all", k.Vm.Strip.Button);
        Assert.Equal("CPU", k.Vm.Gauge.Label);
        Assert.Equal("20%", k.Vm.Live.CpuLoad);
        Assert.Contains(nameof(HomeViewModel.Strip), raised);
        Assert.Contains(nameof(HomeViewModel.Gauge), raised);
    }
}
