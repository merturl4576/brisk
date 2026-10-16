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
        public List<(IReadOnlyList<ActionItem> Items, ActionGroupKind? Group)> Confirms = new();
        public GroupPageViewModel Page(ActionGroupKind g) =>
            new(Session, g, Loc, (items, group) => Confirms.Add((items, group)), () => new[] { "chrome" });
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
        var page = k.Page(ActionGroupKind.Space);
        Assert.Equal(new[] { "space.caches", "space.shaders", "space.downloads", "space.recycle-bin" },
            page.Rows.Select(r => r.Id));
        var shaders = page.Rows[1];
        Assert.False(shaders.IsTicked);
        shaders.IsTicked = true;
        Assert.True(k.Session.IsTicked("space.shaders"));

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
        var page = k.Page(ActionGroupKind.Space);
        var first = page.Rows[0];
        k.Session.SetTicked("space.shaders", true);
        Assert.Same(first, page.Rows[0]);
    }

    [Fact]
    public async Task DownloadsRow_ListsEveryFile_LargestFirst_AndFooterCountsTickedBytes()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Space);
        var downloads = page.Rows.Single(r => r.Id == "space.downloads");
        Assert.Equal(new[] { "a.exe", "b.msi" }, downloads.Files.Select(f => f.Name));
        Assert.True(downloads.HasDetails);

        downloads.Files[0].IsTicked = false;
        Assert.False(k.Session.IsFileTicked(@"C:\D\a.exe"));
        downloads.SelectNoFiles.Execute(null);
        Assert.All(downloads.Files, f => Assert.False(f.IsTicked));
        downloads.SelectAllFiles.Execute(null);
        Assert.All(downloads.Files, f => Assert.True(f.IsTicked));

        Assert.Equal("3 seçili · 13 GB", page.Footer);   // caches, downloads, bin
    }

    [Fact]
    public async Task RunGroup_WithRecycleBin_AsksTheShell()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Space);
        Assert.True(page.RunGroup.CanExecute(null));
        page.RunGroup.Execute(null);
        var asked = Assert.Single(k.Confirms);
        Assert.Equal(ActionGroupKind.Space, asked.Group);
        Assert.Empty(k.Backend.Runs);
    }

    [Fact]
    public async Task RunGroup_PrivacyOnly_RunsDirectly()
    {
        var k = await Make();
        var page = k.Page(ActionGroupKind.Privacy);
        page.RunGroup.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Done, 3000));
        Assert.Equal(new[] { "privacy.ads" }, k.Backend.Runs.Single().RowIds.ToArray());
        Assert.Empty(k.Confirms);
        Assert.Equal("1 seçili", page.Footer);
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
        await k.Session.RunAsync(null, skipUnrecoverable: true);
        var privacy = k.Page(ActionGroupKind.Privacy).Rows.Single(r => r.Id == "privacy.ads");
        Assert.Equal("Kapandı ✓", privacy.Result);
        Assert.Equal(ResultTone.Good, privacy.ResultTone);
        var failed = k.Page(ActionGroupKind.Speed).Rows.Single(r => r.Id == "speed.animations");
        Assert.Equal("Olmadı", failed.Result);
        Assert.Equal("access denied", failed.ResultReason);
        var deferred = k.Page(ActionGroupKind.Space).Rows.Single(r => r.Id == "space.caches");
        Assert.Equal("chrome kapanınca temizlenecek", deferred.Result);
    }

    [Fact]
    public async Task Rows_RetitleOnLanguageChange()
    {
        var k = await Make(b => b.Space = new ActionGroup(ActionGroupKind.Space, new[]
        {
            ActionPlanBuilder.ItemFrom(ActionCatalog.Find("space.caches"), 100),
        }));
        var row = k.Page(ActionGroupKind.Space).Rows.Single();
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
        var page = k.Page(ActionGroupKind.Space);
        page.Expand("space.downloads");
        Assert.True(page.Rows.Single(r => r.Id == "space.downloads").IsExpanded);
    }

    [Fact]
    public async Task RunGroup_IsDisabledWhileRunning()
    {
        var k = await Make();
        k.Backend.HoldRun = new ManualResetEventSlim(false);
        var page = k.Page(ActionGroupKind.Privacy);
        var run = k.Session.RunAsync(null, skipUnrecoverable: true);
        Assert.True(SpinWait.SpinUntil(() => k.Session.State == SessionState.Running, 2000));
        Assert.False(page.RunGroup.CanExecute(null));
        k.Backend.HoldRun.Set();
        await run;
    }
}
