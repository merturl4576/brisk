using BriskEngine.Actions;
using BriskEngine.Cleaning;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Tests.Snapshots;
using Brisk.Ui;
using Brisk.Ui.Controls;
using Brisk.Ui.Pages;

namespace Brisk.Tests.Ui;

/// Yer aç: MIN / MID / MAX. The typical backend's rows: caches 6 000 000 000
/// at MIN; shaders 2 000 000 000, downloads 300 (two files) and the recycle
/// bin 8 000 000 000 at MID; nothing at MAX. Free space reads 100 000 000 000.
public sealed class SpacePageViewModelTests
{
    private static readonly object Gate = new();

    private sealed class Kit
    {
        public FakeBackend Backend = FakeBackend.Typical();
        public Loc Loc = new();
        public ActionSession Session = null!;

        public SpacePageViewModel Page()
        {
            var page = new SpacePageViewModel(Session, Loc, () => new[] { "chrome" }, Backend.SystemDriveFreeBytes,
                a => a(), a => { lock (Gate) a(); });
            page.Enter();
            return page;
        }
    }

    private static Kit New(Action<FakeBackend>? setup = null)
    {
        var k = new Kit();
        setup?.Invoke(k.Backend);
        k.Loc.SetLanguage("tr");
        k.Session = new ActionSession(k.Backend, a => { lock (Gate) a(); });
        return k;
    }

    private static async Task<Kit> Make(Action<FakeBackend>? setup = null)
    {
        var k = New(setup);
        await k.Session.LookAsync();
        return k;
    }

    private static void AddRows(FakeBackend b, params ActionItem[] rows) =>
        b.Space = b.Space! with { Items = b.Space.Items.Concat(rows).ToList() };

    private static string[] Tiles(SpacePageViewModel page) =>
        page.Levels.Select(t => $"{t.Number} {t.Unit}").ToArray();

    /// Each tile holds its level and every level under it, known sizes only
    /// (a row with no size adds nothing and shows "—"), minus skips. The
    /// caption counts every known size, whatever the level and the skips
    /// (ruling R5); the button and the gauge are the selected tile's number.
    [Fact]
    public async Task Tiles_show_cumulative_known_bytes()
    {
        var k = await Make(b => AddRows(b,
            FakeBackend.Row("space.restore-points", ActionGroupKind.Space, null, level: SpaceLevel.Max, lost: true),
            FakeBackend.Row("space.archives", ActionGroupKind.Space, 1_000_000_000, level: SpaceLevel.Max, lost: true)));
        var page = k.Page();

        Assert.Equal(new[] { "MIN", "MID", "MAX" }, page.Levels.Select(t => t.Name));
        Assert.Equal(new[] { "#5FD4E8", "#FFB020", "#FF3B3B" }, page.Levels.Select(t => t.Color));
        Assert.Equal(new[] { "Kendiliğinden geri gelir", "Küçük bir bedeli var", "Geri alınamayanlar dahil" },
            page.Levels.Select(t => t.Tagline));
        // 6 000 000 000; + 10 000 000 300; + 1 000 000 000 (restore points: no size).
        Assert.Equal(new[] { "5,6 GB", "15 GB", "16 GB" }, Tiles(page));
        Assert.True(page.Levels[0].IsSelected);
        Assert.Equal("5,6 GB boşalt", page.Button);
        Assert.Equal(6_000_000_000, page.Gauge.Bytes);
        Assert.Equal("16 GB gereksiz dosya bulundu", page.Gauge.Caption);
        Assert.Equal("—", page.Rows.Single(r => r.Id == "space.restore-points").Value);
        Assert.Equal("#5FD4E8", page.LevelColor);

        page.Levels[1].Select.Execute(null);
        Assert.Equal(SpaceLevel.Mid, k.Session.Level);
        Assert.True(page.Levels[1].IsSelected);
        Assert.False(page.Levels[0].IsSelected);
        Assert.Equal("15 GB boşalt", page.Button);
        Assert.Equal(16_000_000_300, page.Gauge.Bytes);
        Assert.Equal("#FFB020", page.LevelColor);

        k.Session.ToggleSkip("space.recycle-bin");
        Assert.Equal(new[] { "5,6 GB", "7,5 GB", "8,4 GB" }, Tiles(page));
        Assert.Equal("7,5 GB boşalt", page.Button);
        Assert.Equal("16 GB gereksiz dosya bulundu", page.Gauge.Caption);   // the caption stays

        // C: block: free now, and free after the selected level goes.
        Assert.Equal("93 GB", page.FreeNow);
        Assert.Equal("Sonra boş", page.FreeAfterLabel);
        Assert.Equal(Sizes.Format(100_000_000_000 + 8_000_000_300, k.Loc.Culture), page.FreeAfter);
    }

    /// The tiles stay live while the look goes (ruling R22): a level can be
    /// picked before the rows are in, and the numbers read "—" until then.
    [Fact]
    public async Task Tiles_stay_live_during_the_look()
    {
        var k = New(b => b.HoldScan = new ManualResetEventSlim(false));
        var look = k.Session.LookAsync();
        var page = k.Page();
        Assert.Equal(new[] { "— ", "— ", "— " }, Tiles(page));
        Assert.True(page.Levels[2].Select.CanExecute(null));
        page.Levels[2].Select.Execute(null);
        Assert.Equal(SpaceLevel.Max, k.Session.Level);
        k.Backend.HoldScan!.Set();
        await look;
        Assert.Equal("15 GB boşalt", page.Button);
    }

    /// Rows sit under their level's header; a row above the selected level
    /// is locked: dimmed, no tap. Picking its level unlocks it, and its skip
    /// comes back when the level is picked again.
    [Fact]
    public async Task Rows_above_level_are_locked()
    {
        var k = await Make();
        var page = k.Page();
        Assert.Equal(new[] { "MIN", "MID", "MAX" }, page.Groups.Select(g => g.Name));
        Assert.Equal(new[] { "space.caches" }, page.Groups[0].Rows.Select(r => r.Id));
        var mid = page.Groups[1];
        Assert.Equal(new[] { "space.shaders", "space.downloads", "space.recycle-bin" }, mid.Rows.Select(r => r.Id));
        Assert.False(page.Groups[2].HasRows);                      // nothing at MAX on this machine
        Assert.False(page.Groups[0].Locked);
        Assert.False(page.Groups[0].Rows[0].Locked);
        Assert.True(mid.Locked);
        Assert.All(mid.Rows, r => Assert.True(r.Locked));
        Assert.All(mid.Rows, r => Assert.Equal("MID", r.LevelName));

        var shaders = mid.Rows[0];
        shaders.TapCommand.Execute(null);                          // locked: the tap does nothing
        Assert.False(k.Session.IsSkipped("space.shaders"));
        Assert.Equal("5,6 GB boşalt", page.Button);

        page.Levels[1].Select.Execute(null);
        Assert.False(mid.Locked);
        Assert.False(shaders.Locked);
        shaders.TapCommand.Execute(null);
        Assert.True(shaders.Skipped);
        Assert.Equal("13 GB boşalt", page.Button);                 // 16 000 000 300 − 2 000 000 000

        page.Levels[0].Select.Execute(null);
        Assert.True(shaders.Locked);
        page.Levels[1].Select.Execute(null);
        Assert.True(shaders.Skipped);                              // the skip is kept
    }

    /// A row that cannot come back says so once, in amber: its own cost when
    /// it has one (the Downloads row's "Geri alınamaz."), the plain words
    /// when it has none.
    [Fact]
    public async Task Lost_rows_warn_once_in_amber()
    {
        var k = await Make(b => b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.developer"), 100),
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.downloads"), 100),
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.recycle-bin"), 100),
        }));
        var page = k.Page();
        var developer = page.Rows.Single(r => r.Id == "space.developer");
        Assert.Equal("Sonra yeniden iner.", developer.Note);
        Assert.Null(developer.Warning);
        var downloads = page.Rows.Single(r => r.Id == "space.downloads");
        Assert.Equal("Geri alınamaz.", downloads.Warning);
        Assert.Null(downloads.Note);
        var bin = page.Rows.Single(r => r.Id == "space.recycle-bin");
        Assert.Equal("Geri alınamaz.", bin.Warning);
        Assert.Null(bin.Note);
    }

    /// The user's own big files are listed once the walk lands, locked, and
    /// never reach a tile, the gauge, the caption, the button or the C: block.
    [Fact]
    public async Task Personal_files_never_in_totals()
    {
        var hold = new ManualResetEventSlim(false);
        var k = await Make(b =>
        {
            b.Personal.Add(new PersonalFile("kali.vhdx", @"C:\VMs\kali.vhdx", 60L << 30));
            b.HoldPersonal = hold;
        });
        var page = k.Page();
        Assert.False(page.ShowMine);                               // the walk has not landed
        Assert.Empty(page.Mine);

        hold.Set();
        Assert.True(SpinWait.SpinUntil(() => page.ShowMine, 3000));
        var mine = Assert.Single(page.Mine);
        Assert.Equal("kali.vhdx", mine.Name);
        Assert.Equal("60 GB", mine.Size);
        Assert.Equal("Senin dosyaların · brisk dokunmaz", page.MineTitle);

        page.Levels[2].Select.Execute(null);
        Assert.Equal(new[] { "5,6 GB", "15 GB", "15 GB" }, Tiles(page));
        Assert.Equal(16_000_000_300, page.Gauge.Bytes);
        Assert.Equal("15 GB gereksiz dosya bulundu", page.Gauge.Caption);
        Assert.Equal("15 GB boşalt", page.Button);
        Assert.Equal(Sizes.Format(100_000_000_000 + 16_000_000_300, k.Loc.Culture), page.FreeAfter);
        Assert.DoesNotContain(page.Rows, r => r.Id.Contains("kali", StringComparison.OrdinalIgnoreCase));
    }

    /// The run's done caption follows the level it ran at; the button says
    /// "✓ Tamam", the C: block "Şimdi boş", and the tiles rest. No Geri al.
    [Theory]
    [InlineData(SpaceLevel.Min, "Önbellekler zamanla geri gelir", 6_000_000_000)]
    [InlineData(SpaceLevel.Mid, "Kurulum dosyaları geri gelmez", 16_000_000_300)]
    [InlineData(SpaceLevel.Max, "Silinenlerin bir kısmı geri gelmez", 16_000_000_300)]
    public async Task Done_caption_per_level(SpaceLevel level, string caption, long freed)
    {
        var k = await Make();
        var page = k.Page();
        page.Levels[(int)level].Select.Execute(null);
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done && !k.Session.Busy, 3000));

        Assert.Equal(PagePhase.Done, page.Phase);
        Assert.Equal("Boşaldı", page.Gauge.Label);
        Assert.True(page.Gauge.Green);
        Assert.Equal(freed, page.Gauge.Bytes);
        Assert.Equal(caption, page.Gauge.Caption);
        Assert.Equal("✓ Tamam", page.Button);
        Assert.True(page.ButtonOk);
        Assert.Equal("Şimdi boş", page.FreeAfterLabel);
        Assert.Equal(Sizes.Format(100_000_000_000 + freed, k.Loc.Culture), page.FreeAfter);
        Assert.All(page.Levels, t => Assert.False(t.Select.CanExecute(null)));
        Assert.Equal("✓ silindi", page.Rows.Single(r => r.Id == "space.caches").Value);
    }

    /// Hepsini yap runs Yer aç at MIN, whatever level the page shows: its
    /// caption is MIN's.
    [Fact]
    public async Task Run_all_done_caption_is_mins()
    {
        var k = await Make();
        var page = k.Page();
        page.Levels[2].Select.Execute(null);
        await k.Session.RunAllAsync();
        Assert.Equal(PagePhase.Done, page.Phase);
        Assert.Equal("Önbellekler zamanla geri gelir", page.Gauge.Caption);
    }

    /// The scan reveals rows by id (carry from Task 11): rows the scan has
    /// shown stay shown when the list is rebuilt in another order.
    [Fact]
    public async Task Reveal_is_keyed_on_the_row()
    {
        var k = await Make();
        var timers = new ManualTimers();
        var page = new SpacePageViewModel(k.Session, k.Loc, () => Array.Empty<string>(), () => null,
            a => a(), a => a(), timers, () => true);
        var look = k.Session.RescanAsync();
        await look;
        page.Enter();
        page.Activate();
        timers.Advance(TimeSpan.FromMilliseconds(300));
        Assert.Equal(new[] { "space.caches" }, page.Rows.Where(r => r.Revealed).Select(r => r.Id));
        page.Levels[1].Select.Execute(null);
        timers.Advance(TimeSpan.FromMilliseconds(300));
        var shown = page.Rows.Where(r => r.Revealed).Select(r => r.Id).ToList();
        Assert.Equal(new[] { "space.caches", "space.shaders" }, shown);
        timers.Advance(TimeSpan.FromSeconds(3));
        Assert.All(page.Rows, r => Assert.True(r.Revealed));
        Assert.Equal(PagePhase.Ready, page.Phase);
    }

    /// Brisk's moving background off (ruling R30): the MAX tile's fire is one
    /// static frame — drawn, not ticking, and still after time passes. Hot
    /// (selected, focused or hovered) lifts it from 0.3 to 0.85 at once.
    [Fact]
    public void Fire_static_under_reduced_motion()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var timers = new ManualTimers();
            var clock = new AmbientClock(() => false);
            clock.SetVisible(true);
            var fire = new PixelFire(clock, timers, new Random(7));
            Assert.False(fire.Running);
            Assert.Equal(0, timers.Live);
            Assert.Equal(1, fire.Frames);
            Assert.True(fire.LitCells > PixelFire.Columns, "the static frame shows flames above the source row");
            Assert.Equal(PixelFire.RestOpacity, fire.Opacity);

            timers.Advance(TimeSpan.FromSeconds(2));
            Assert.Equal(1, fire.Frames);

            fire.Hot = true;
            Assert.Equal(PixelFire.HotOpacity, fire.Opacity);
            Assert.Equal(2, fire.Frames);                          // one new still frame at the hotter heat
            Assert.Equal(0, timers.Live);
            fire.Hot = false;
            Assert.Equal(PixelFire.RestOpacity, fire.Opacity);
        });
    }
}
