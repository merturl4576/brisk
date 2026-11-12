using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Ui.Pages;

namespace Brisk.Tests.Ui;

public sealed class GroupPageViewModelTests
{
    private static readonly object Gate = new();

    private sealed class Kit
    {
        public FakeBackend Backend = FakeBackend.Typical();
        public Loc Loc = new();
        public ActionSession Session = null!;
        public GroupPageViewModel Page(ActionGroupKind g) => new(Session, g, Loc, () => new[] { "chrome" });

        /// Yer aç has its own page; its rows are the same RowViewModel.
        public SpacePageViewModel Space() => new(Session, Loc, () => new[] { "chrome" }, () => null,
            a => a(), a => { lock (Gate) a(); });
    }

    private static async Task<Kit> Make(Action<FakeBackend>? setup = null)
    {
        var k = new Kit();
        setup?.Invoke(k.Backend);
        k.Loc.SetLanguage("tr");
        k.Session = new ActionSession(k.Backend, a => { lock (Gate) a(); });
        await k.Session.LookAsync();
        return k;
    }

    [Fact]
    public async Task Rows_FollowThePlanOrder_AndTickTheSession()
    {
        var k = await Make();
        var page = k.Space();
        Assert.Equal(new[] { "space.caches", "space.shaders", "space.downloads", "space.recycle-bin" },
            page.Rows.Select(r => r.Id));
        var shaders = page.Rows[1];
        Assert.False(shaders.IsTicked);
        shaders.IsTicked = true;                                // MID, above the level: locked
        Assert.False(k.Session.IsTicked("space.shaders"));
        k.Session.SetLevel(SpaceLevel.Mid);
        Assert.True(shaders.IsTicked);
        shaders.IsTicked = false;
        Assert.True(k.Session.IsSkipped("space.shaders"));

        var raised = new List<string?>();
        page.Rows[0].PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        k.Session.SetTicked("space.caches", false);
        Assert.False(page.Rows[0].IsTicked);
        Assert.Contains(nameof(RowViewModel.IsTicked), raised);
    }

    [Fact]
    public async Task Rows_KeepTheirInstances_WhenOnlyTicksChange()
    {
        var k = await Make();
        var page = k.Space();
        var first = page.Rows[0];
        k.Session.ToggleSkip("space.recycle-bin");
        Assert.Same(first, page.Rows[0]);
    }

    [Fact]
    public async Task DownloadsRow_ListsEveryFile_LargestFirst_AndFooterCountsTickedBytes()
    {
        var k = await Make();
        k.Session.SetLevel(SpaceLevel.Mid);                     // the Downloads row is MID
        var page = k.Space();
        var downloads = page.Rows.Single(r => r.Id == "space.downloads");
        Assert.Equal(new[] { "a.exe", "b.msi" }, downloads.Files.Select(f => f.Name));
        Assert.True(downloads.HasDetails);

        downloads.Files[0].IsTicked = false;
        Assert.False(k.Session.IsFileTicked(@"C:\D\a.exe"));
        downloads.SelectNoFiles.Execute(null);
        Assert.All(downloads.Files, f => Assert.False(f.IsTicked));
        downloads.SelectAllFiles.Execute(null);
        Assert.All(downloads.Files, f => Assert.True(f.IsTicked));

        Assert.Equal("15 GB boşalt", page.Button);   // caches, shaders, downloads, bin
    }

    /// No confirmation: the Space page runs its level at once, the Recycle
    /// Bin (MID, cannot come back) included.
    [Fact]
    public async Task RunGroup_Space_RunsWithoutAsking()
    {
        var k = await Make();
        k.Session.SetLevel(SpaceLevel.Mid);
        var page = k.Space();
        Assert.True(page.RunGroup.CanExecute(null));
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Equal(new[] { "space.caches", "space.downloads", "space.recycle-bin", "space.shaders" },
            k.Backend.Runs.Single().RowIds.OrderBy(i => i, StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunGroup_PrivacyOnly_RunsDirectly()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Privacy);
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Equal(new[] { "privacy.ads" }, k.Backend.Runs.Single().RowIds.ToArray());
        Assert.True(SpinWait.SpinUntil(() => page.Button == "✓ Tamam", 3000));
    }

    [Fact]
    public async Task Rows_ShowResultWordsAfterARun()
    {
        var k = await Make(b => b.Outcome = i => i.Id switch
        {
            "speed.animations" => new RowResult(i.Id, RowOutcome.Failed, "access denied", null, 0, 0),
            "space.caches" => new RowResult(i.Id, RowOutcome.Done, null, null, 0, 3L << 30),
            _ => new RowResult(i.Id, RowOutcome.Done, null, null, 0, 0),
        });
        await k.Session.RunAllAsync();
        var privacy = k.Page(ActionGroupKind.Privacy).Rows.Single(r => r.Id == "privacy.ads");
        Assert.Equal("✓ kapandı", privacy.Result);
        Assert.Equal(ResultTone.Good, privacy.ResultTone);
        var failed = k.Page(ActionGroupKind.Speed).Rows.Single(r => r.Id == "speed.animations");
        Assert.Equal("Olmadı", failed.Result);
        Assert.Equal("access denied", failed.ResultReason);
        var deferred = k.Space().Rows.Single(r => r.Id == "space.caches");
        Assert.Equal("chrome kapanınca temizlenecek", deferred.Result);
    }

    [Fact]
    public async Task SkippedRows_ShowNoEngineReason_InAMutedTone()
    {
        var k = await Make(b => b.Outcome = i => i.Id == "space.caches"
            ? new RowResult(i.Id, RowOutcome.Skipped, "nothing to clean", null, 0, 0)
            : new RowResult(i.Id, RowOutcome.Done, null, null, 0, 0));
        await k.Session.RunAllAsync();
        var row = k.Space().Rows.Single(r => r.Id == "space.caches");
        Assert.Equal("Atlandı", row.Result);
        Assert.Equal(ResultTone.Muted, row.ResultTone);
        Assert.Null(row.ResultReason);
    }

    [Fact]
    public async Task Rows_RetitleOnLanguageChange()
    {
        var k = await Make(b => b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.caches"), 100),
        }));
        var row = k.Space().Rows.Single();
        var turkish = row.Title;
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        k.Loc.SetLanguage("en");
        Assert.Equal("Caches and temporary files", row.Title);
        Assert.NotEqual(turkish, row.Title);
        Assert.Contains(nameof(RowViewModel.Title), raised);
    }

    [Fact]
    public async Task Expand_OpensTheRow()
    {
        var k = await Make();
        var page = k.Space();
        page.Expand("space.downloads");
        Assert.True(page.Rows.Single(r => r.Id == "space.downloads").IsExpanded);
    }

    [Fact]
    public async Task RunGroup_IsDisabledWhileRunning()
    {
        var k = await Make();
        k.Backend.HoldRun = new ManualResetEventSlim(false);
        var page = k.Page(ActionGroupKind.Privacy);
        var run = k.Session.RunAllAsync();
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Running, 2000));
        Assert.False(page.RunGroup.CanExecute(null));
        k.Backend.HoldRun.Set();
        await run;
    }

    // ---- the Hızlandır and Gizlilik pages (Task 11)

    /// Every timer step runs under the session's lock, so a change landing
    /// from a look's thread never interleaves with a tick.
    private static void Step(ManualTimers timers, int ms)
    {
        lock (Gate) timers.Advance(TimeSpan.FromMilliseconds(ms));
    }

    private static int Shown(GroupPageViewModel page) => page.Main.Concat(page.Also).Count(r => r.Revealed);

    /// Entering the page plays the scan: one row every 300 ms, and only rows
    /// the look has delivered. Before Speed's half arrives nothing shows,
    /// whatever the clock does; the number is the shown rows that will run.
    [Fact]
    public async Task Reveal_never_outruns_results()
    {
        var hold = new ManualResetEventSlim(false);
        var k = new Kit();
        k.Backend.HoldFast = hold;
        k.Loc.SetLanguage("tr");
        k.Session = new ActionSession(k.Backend, a => { lock (Gate) a(); });
        var timers = new ManualTimers();
        var page = new GroupPageViewModel(k.Session, ActionGroupKind.Speed, k.Loc, () => new[] { "chrome" },
            timers, () => true);
        page.Enter();
        page.Activate();

        var look = k.Session.LookAsync();
        Assert.True(SpinWait.SpinUntil(() => k.Session.SpaceReady, 3000));   // the other half is in
        for (var i = 0; i < 5; i++)
        {
            Step(timers, 300);
            Assert.Equal(PagePhase.Scanning, page.Phase);
            Assert.Equal(0, Shown(page));
            Assert.Equal("0", page.Gauge.Number);
            Assert.False(page.ShowButton);
            Assert.False(page.ShowRight);
        }
        Assert.Equal("Açılış taranıyor…", page.Gauge.Label);

        hold.Set();
        await look;
        Assert.True(SpinWait.SpinUntil(() => k.Session.PersonalFilesReady, 3000));
        Assert.Equal(0, Shown(page));                        // the data is in; the reveal has not stepped yet

        Step(timers, 300);                                   // Spotify
        Assert.Equal(1, Shown(page));
        Assert.Equal("1", page.Gauge.Number);
        Assert.Equal(PagePhase.Scanning, page.Phase);
        Step(timers, 300);                                   // animations
        Assert.Equal(2, Shown(page));
        Assert.Equal("2", page.Gauge.Number);
        Assert.True(Shown(page) <= page.Main.Count + page.Also.Count);
        Step(timers, 300);                                   // the scan ends a step after the last row
        Assert.Equal(PagePhase.Ready, page.Phase);
        Assert.True(page.ShowButton);
        Assert.True(page.ShowRight);                         // OneDrive, off by default: İstersen ekle
        Assert.Equal(new[] { "startup:HKCU:OneDrive" }, page.Extra.Select(r => r.Id));
        Assert.Equal("2 değişikliği yap", page.Button);

        // A new look restarts the scan on the page being looked at.
        hold.Reset();
        var again = k.Session.RescanAsync();
        Assert.True(SpinWait.SpinUntil(() => k.Session.SpaceReady, 3000));
        Step(timers, 900);
        Assert.Equal(PagePhase.Scanning, page.Phase);
        Assert.Equal(0, Shown(page));
        hold.Set();
        await again;
        Assert.True(SpinWait.SpinUntil(() => k.Session.PersonalFilesReady, 3000));

        // Motion off: every delivered row at once, and no scan once the data is in.
        var still = new GroupPageViewModel(k.Session, ActionGroupKind.Speed, k.Loc, () => new[] { "chrome" },
            timers, () => false);
        still.Enter();
        still.Activate();
        Assert.Equal(PagePhase.Ready, still.Phase);
        Assert.Equal(2, Shown(still));

        // Leaving stops the page's timer.
        page.Deactivate();
        still.Deactivate();
        Assert.Equal(0, timers.Live);
    }

    /// A tap skips a row or takes it back; a row off by default is added.
    /// The number, the button and the nav badge use the same count.
    [Fact]
    public async Task Tap_toggles_skip_and_total()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Speed);
        Assert.Equal(new[] { "startup:HKCU:Spotify" }, page.Main.Select(r => r.Id));
        Assert.Equal(new[] { "speed.animations" }, page.Also.Select(r => r.Id));
        Assert.Equal(2, page.SelectedCount);
        Assert.Equal("2", page.Gauge.Number);
        Assert.Equal("değişiklik", page.Gauge.Unit);

        var spotify = page.Main.Single();
        Assert.Equal("durdur", spotify.Value);
        spotify.Tap();
        Assert.True(spotify.Skipped);
        Assert.True(spotify.Off);
        Assert.True(k.Session.IsSkipped("startup:HKCU:Spotify"));
        Assert.Equal(1, page.SelectedCount);
        Assert.Equal("1", page.Gauge.Number);
        Assert.Equal("1 ayarı değiştir", page.Button);
        spotify.TapCommand.Execute(null);
        Assert.False(spotify.Skipped);
        Assert.Equal("2 değişikliği yap", page.Button);

        page.Also.Single().Tap();
        Assert.Equal("1 uygulamayı durdur", page.Button);
        Assert.Equal("Açılışta durdurulacak", page.Gauge.Label);
        Assert.Equal("uygulama", page.Gauge.Unit);
        Assert.Equal("Windows ile açılan 2 uygulamadan", page.Gauge.Caption);

        var onedrive = page.Extra.Single();
        Assert.Equal("+ ekle", onedrive.Value);
        Assert.False(onedrive.Off);
        onedrive.Tap();
        Assert.True(onedrive.Added);
        Assert.Equal("✓ eklendi", onedrive.Value);
        Assert.Equal(2, page.SelectedCount);
        Assert.Equal("2 uygulamayı durdur", page.Button);

        // Privacy: "İstersen ekle" holds location; the button says "kapat".
        var privacy = k.Page(ActionGroupKind.Privacy);
        Assert.Equal("1 ayarı kapat", privacy.Button);
        privacy.Extra.Single().Tap();
        Assert.Equal("2 ayarı kapat", privacy.Button);
        Assert.Equal("Bilgisayarından dışarı veri gönderen ayarlar", privacy.Gauge.Caption);
    }

    /// Bırakıyorum: a startup app off by default with a cost (brisk protects
    /// it). It says "kalsın" and a tap does nothing.
    [Fact]
    public async Task Protected_startup_apps_stay_and_cannot_be_tapped()
    {
        var k = await Make(b => b.Fast[0] = b.Fast[0] with
        {
            Items = b.Fast[0].Items.Append(new ActionItem("startup:HKLM:SecurityHealth", ActionGroupKind.Speed, null,
                "Windows Güvenliği", null, "Virus protection", null, null, null, false, false, true, true,
                ActionLinkKind.Rules, new[] { "startup:HKLM:SecurityHealth" }, Array.Empty<ActionFile>())).ToList(),
        });
        var page = k.Page(ActionGroupKind.Speed);
        var keep = page.Keep.Single();
        Assert.Equal("startup:HKLM:SecurityHealth", keep.Id);
        Assert.Equal("kalsın", keep.Value);
        keep.Tap();
        Assert.True(k.Session.IsSkipped(keep.Id));
        Assert.Equal(2, page.SelectedCount);
    }

    [Fact]
    public async Task Button_disabled_when_nothing_selected()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Privacy);
        Assert.True(page.RunGroup.CanExecute(null));
        page.Main.Single().Tap();
        Assert.Equal(0, page.SelectedCount);
        Assert.Equal("Hiçbir şey seçili değil", page.Button);
        Assert.False(page.RunGroup.CanExecute(null));
        Assert.Equal("0", page.Gauge.Number);
        page.RunGroup.Execute(null);
        Assert.Empty(k.Backend.Runs);

        var speed = k.Page(ActionGroupKind.Speed);
        foreach (var r in speed.Main.Concat(speed.Also).ToList()) r.Tap();
        Assert.Equal("Hiçbir şey seçili değil", speed.Button);
        Assert.False(speed.RunGroup.CanExecute(null));
    }

    /// The run turns the rows green with their own word, the gauge says
    /// what happened, the button reads "✓ Tamam" and Geri al shows; it
    /// stays for that run after a rescan (ruling R31), and only on the page
    /// whose settings the run changed.
    [Fact]
    public async Task Undo_visible_after_done()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Speed);
        var privacy = k.Page(ActionGroupKind.Privacy);
        Assert.False(page.ShowUndo);

        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done && !k.Session.Busy, 3000));
        Assert.Equal(PagePhase.Done, page.Phase);
        Assert.Equal("✓ Tamam", page.Button);
        Assert.True(page.ButtonOk);
        Assert.False(page.RunGroup.CanExecute(null));
        Assert.True(page.ShowUndo);
        Assert.True(page.Undo.CanExecute(null));
        Assert.Equal("✓ durdu", page.Main.Single().Value);
        Assert.True(page.Main.Single().Done);
        Assert.Equal("✓ kapandı", page.Also.Single().Value);
        Assert.Equal("Değiştirildi", page.Gauge.Label);
        Assert.Equal("2", page.Gauge.Number);
        Assert.True(page.Gauge.Green);
        Assert.Equal("Farkı bir sonraki açılışta görürsün", page.Gauge.Caption);
        Assert.False(privacy.ShowUndo);
        Assert.Equal("Tekrar tara", privacy.Button);           // run once, then rescan (ruling R37)

        await k.Session.RescanAsync();
        Assert.Equal(PagePhase.Ready, page.Phase);
        Assert.True(page.ShowUndo);
        Assert.Equal("2 değişikliği yap", page.Button);
        Assert.False(privacy.ShowUndo);

        page.Undo.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.UndoResults is not null && k.Session.State == SessionState.Ready
            && !k.Session.Busy, 3000));
        Assert.Contains("startup:HKCU:Spotify", k.Backend.UndoneRules);
        Assert.False(page.ShowUndo);
    }

    /// Yer aç has its own page; a GroupPage for it would show no files and no levels.
    [Fact]
    public async Task A_group_page_for_space_is_refused()
    {
        var k = await Make();
        Assert.Throws<ArgumentException>(() => k.Page(ActionGroupKind.Space));
    }

    /// Ruling R56: one run = one undo. After Hepsini yap, Geri al on
    /// Hızlandır and on Gizlilik says it reverts both pages; after a page
    /// run it keeps the plain copy.
    [Fact]
    public async Task Undo_after_RunAll_names_both_pages()
    {
        var k = await Make();
        var speed = k.Page(ActionGroupKind.Speed);
        var privacy = k.Page(ActionGroupKind.Privacy);
        Assert.Equal("Geri al", speed.UndoText);

        await k.Session.RunAllAsync();
        Assert.True(speed.ShowUndo);
        Assert.True(privacy.ShowUndo);
        Assert.Equal("Geri al (Hızlandır + Gizlilik)", speed.UndoText);
        Assert.Equal("Geri al (Hızlandır + Gizlilik)", privacy.UndoText);
        k.Loc.SetLanguage("en");
        Assert.Equal("Undo (Speed up + Privacy)", speed.UndoText);
    }

    [Fact]
    public async Task Undo_after_a_page_run_keeps_the_plain_copy()
    {
        var k = await Make();
        var speed = k.Page(ActionGroupKind.Speed);
        var raised = new List<string?>();
        speed.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await k.Session.RunAsync(ActionGroupKind.Speed);
        Assert.True(speed.ShowUndo);
        Assert.Equal("Geri al", speed.UndoText);
        Assert.Contains(nameof(GroupPageViewModel.UndoText), raised);
    }

    /// Every row failed: no green, no "✓ Tamam"; the gauge says Olmadı and
    /// the button offers a fresh look.
    [Fact]
    public async Task A_run_where_everything_failed_offers_a_rescan()
    {
        var k = await Make(b => b.Outcome = i => new RowResult(i.Id, RowOutcome.Failed, "access denied", null, 0, 0));
        var page = k.Page(ActionGroupKind.Privacy);
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done && !k.Session.Busy, 3000));
        Assert.Equal(PagePhase.Done, page.Phase);
        Assert.Equal("Olmadı", page.Gauge.Label);
        Assert.True(page.Gauge.Bad);
        Assert.False(page.Gauge.Green);
        Assert.Equal("Tekrar tara", page.Button);
        Assert.False(page.ButtonOk);
        Assert.False(page.ShowUndo);
        Assert.Equal("Olmadı", page.Main.Single().Value);
        Assert.True(page.RunGroup.CanExecute(null));
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Ready && !k.Session.Busy, 3000));
        Assert.Equal("1 ayarı kapat", page.Button);
    }

    /// While a run goes the page says so and nothing is tappable.
    [Fact]
    public async Task Running_page_says_so_and_ignores_taps()
    {
        var k = await Make();
        k.Backend.HoldRun = new ManualResetEventSlim(false);
        var page = k.Page(ActionGroupKind.Privacy);
        var run = k.Session.RunAsync(ActionGroupKind.Privacy);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Running, 2000));
        Assert.Equal(PagePhase.Running, page.Phase);
        Assert.Equal("Kapatılıyor…", page.Button);
        Assert.True(page.ButtonBusy);
        Assert.Equal("Kapatılıyor…", page.Gauge.Label);
        page.Extra.Single().Tap();
        Assert.False(page.Extra.Single().Added);
        k.Backend.HoldRun.Set();
        await run;
    }

    /// The Speed caption says what the number holds (R39): out of the apps
    /// that start with Windows only when every row is an app; otherwise the
    /// apps and the settings apart. The default selection is mixed.
    [Fact]
    public async Task Speed_caption_counts_apps_and_settings_apart()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Speed);
        Assert.Equal("2", page.Gauge.Number);
        Assert.Equal("1 uygulama · 1 ayar", page.Gauge.Caption);

        page.Main.Single().Tap();                                // Spotify out: settings only
        Assert.Equal("1 ayar", page.Gauge.Caption);

        page.Main.Single().Tap();
        page.Also.Single().Tap();                                // animations out: apps only
        Assert.Equal("Windows ile açılan 2 uygulamadan", page.Gauge.Caption);

        k.Loc.SetLanguage("en");
        page.Also.Single().Tap();
        Assert.Equal("1 app · 1 setting", page.Gauge.Caption);
    }

    /// Once the page's run is over a tap changes nothing: not a row that
    /// went through, not "+ ekle" (pages.html:346).
    [Fact]
    public async Task Taps_after_the_run_change_nothing()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Speed);
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Equal(PagePhase.Done, page.Phase);

        var spotify = page.Main.Single();
        Assert.Equal("✓ durdu", spotify.Value);
        spotify.Tap();
        spotify.TapCommand.Execute(null);
        spotify.Tap();
        Assert.False(spotify.Skipped);
        Assert.False(spotify.Off);
        Assert.False(k.Session.IsSkipped("startup:HKCU:Spotify"));
        Assert.Equal("✓ durdu", spotify.Value);

        var onedrive = page.Extra.Single();
        onedrive.Tap();
        Assert.False(onedrive.Added);
        Assert.False(onedrive.Off);
        Assert.Equal("+ ekle", onedrive.Value);
        Assert.Equal("✓ Tamam", page.Button);
    }

    /// A row that needs a restart says so under its cost before the run;
    /// after it, a change waiting for the restart keeps its done word and the
    /// muted "Yeniden başlatınca" line (R40).
    [Fact]
    public async Task Restart_rows_keep_their_note_before_and_after_the_run()
    {
        var k = await Make(b =>
        {
            b.Fast[1] = new ActionGroup(ActionGroupKind.Privacy, b.Fast[1].Items
                .Select(i => i.Id == "privacy.ads" ? i with { NeedsRestart = true } : i).ToArray());
            b.Outcome = i => new RowResult(i.Id,
                i.Id == "privacy.ads" ? RowOutcome.PendingRestart : RowOutcome.Done, null, null, 0, 0);
        });
        var page = k.Page(ActionGroupKind.Privacy);
        var ads = page.Main.Single(r => r.Id == "privacy.ads");
        Assert.Equal("Yeniden başlatınca", ads.RestartText);
        Assert.Equal("kapat", ads.Value);
        Assert.Null(page.Extra.Single().RestartText);

        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Equal("✓ kapandı", ads.Result);
        Assert.Equal("✓ kapandı", ads.Value);
        Assert.True(ads.Done);
        Assert.Equal("Yeniden başlatınca", ads.RestartText);

        k.Loc.SetLanguage("en");
        Assert.Equal("After restart", ads.RestartText);
    }

    /// Speed's half lands before the Space scan: the page's scan ends, and
    /// the button says "Taranıyor…" (disabled) until the whole look is in,
    /// then the count (R41).
    [Fact]
    public async Task Button_waits_for_the_whole_look()
    {
        var hold = new ManualResetEventSlim(false);
        var k = new Kit();
        k.Backend.HoldScan = hold;
        k.Loc.SetLanguage("tr");
        k.Session = new ActionSession(k.Backend, a => { lock (Gate) a(); });
        var page = new GroupPageViewModel(k.Session, ActionGroupKind.Speed, k.Loc, () => new[] { "chrome" },
            new ManualTimers(), () => false);
        page.Enter();
        page.Activate();
        Assert.Equal(PagePhase.Scanning, page.Phase);

        var look = k.Session.LookAsync();
        Assert.True(SpinWait.SpinUntil(() => page.Phase == PagePhase.Ready, 3000));   // the fast half is in
        Assert.Equal(SessionState.Looking, k.Session.State);
        Assert.False(k.Session.SpaceReady);
        Assert.True(page.ShowButton);
        Assert.Equal("Taranıyor…", page.Button);
        Assert.False(page.RunGroup.CanExecute(null));
        Assert.Equal(2, page.Main.Concat(page.Also).Count(r => r.Revealed));

        hold.Set();
        await look;
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Ready, 3000));
        Assert.Equal("2 değişikliği yap", page.Button);
        Assert.True(page.RunGroup.CanExecute(null));
        page.Deactivate();
    }
}
