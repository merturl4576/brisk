using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using Brisk.Localization;
using Brisk.Session;

namespace Brisk.Tests.Session;

/// One place for a row's words (ruling R34): a startup app is stopped, a
/// setting turned on or off by what brisk does to it, and the page button
/// counts apps and settings apart.
public sealed class RowWordsTests
{
    private static Loc Tr()
    {
        var loc = new Loc();
        loc.SetLanguage("tr");
        return loc;
    }

    private static ActionItem App(string name) => FakeBackend.Row($"startup:HKCU:{name}", ActionGroupKind.Speed, title: name);
    private static ActionItem Catalog(string id) => ActionPlanBuilder.ItemFrom(ActionCatalog.Find(id), null);

    [Fact]
    public void Speed_button_names_apps_settings_or_changes()
    {
        var loc = Tr();
        var apps = new[] { App("Spotify"), App("Discord") };
        var settings = new[] { Catalog("speed.animations"), Catalog("speed.windowed-games") };
        Assert.Equal("2 uygulamayı durdur", RowWords.Button(ActionGroupKind.Speed, apps, loc));
        Assert.Equal("2 ayarı değiştir", RowWords.Button(ActionGroupKind.Speed, settings, loc));
        Assert.Equal("3 değişikliği yap", RowWords.Button(ActionGroupKind.Speed, apps.Append(settings[0]).ToList(), loc));
        Assert.Equal("Hiçbir şey seçili değil", RowWords.Button(ActionGroupKind.Speed, Array.Empty<ActionItem>(), loc));
    }

    [Fact]
    public void Privacy_buttons()
    {
        var loc = Tr();
        Assert.Equal("2 ayarı kapat", RowWords.Button(ActionGroupKind.Privacy,
            new[] { Catalog("privacy.ads"), Catalog("privacy.location") }, loc));
        Assert.Equal("Hiçbir şey seçili değil", RowWords.Button(ActionGroupKind.Privacy, Array.Empty<ActionItem>(), loc));
    }

    /// English says "1 app" and "2 apps"; Turkish has one form.
    [Fact]
    public void English_counts_one_and_many()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        Assert.Equal("Stop 1 app", RowWords.Button(ActionGroupKind.Speed, new[] { App("Spotify") }, loc));
        Assert.Equal("Stop 2 apps", RowWords.Button(ActionGroupKind.Speed, new[] { App("Spotify"), App("Discord") }, loc));
        Assert.Equal("1 app", RowWords.Apps(1, loc));
        Assert.Equal("3 settings", RowWords.Settings(3, loc));
        Assert.Equal("1 uygulama", RowWords.Apps(1, Tr()));
    }

    [Fact]
    public void Done_word_follows_the_rows_own_action()
    {
        var loc = Tr();
        Assert.Equal("✓ durdu", RowWords.DoneWord(App("Spotify"), loc));
        Assert.Equal("✓ açıldı", RowWords.DoneWord(Catalog("speed.windowed-games"), loc));
        Assert.Equal("✓ açıldı", RowWords.DoneWord(Catalog("speed.power-mode"), loc));
        Assert.Equal("✓ kapandı", RowWords.DoneWord(Catalog("speed.animations"), loc));
        Assert.Equal("✓ kapandı", RowWords.DoneWord(Catalog("privacy.ads"), loc));
        Assert.Equal("✓ silindi", RowWords.DoneWord(FakeBackend.Row("space.caches", ActionGroupKind.Space, 1), loc));
        Assert.Equal("durdur", RowWords.Verb(App("Spotify"), loc));
        Assert.Equal("aç", RowWords.Verb(Catalog("speed.gpu-scheduling"), loc));
        Assert.Equal("kapat", RowWords.Verb(Catalog("speed.game-recording"), loc));
    }

    [Fact]
    public void Count_splits_apps_from_settings_and_leaves_space_out()
    {
        var (apps, settings) = RowWords.Count(new[]
        {
            App("Spotify"), Catalog("speed.animations"), Catalog("privacy.ads"),
            FakeBackend.Row("space.caches", ActionGroupKind.Space, 1),
        });
        Assert.Equal(1, apps);
        Assert.Equal(2, settings);
    }

    /// What went through: done or waiting for a restart, and not put back by Windows.
    [Fact]
    public void Succeeded_excludes_what_windows_put_back()
    {
        Assert.True(RowWords.Succeeded(new RowResult("a", RowOutcome.Done, null, null, 0, 0)));
        Assert.True(RowWords.Succeeded(new RowResult("a", RowOutcome.PendingRestart, null, null, 0, 0)));
        Assert.True(RowWords.Succeeded(new RowResult("a", RowOutcome.Done, null, ReadBackState.Held, 0, 0)));
        Assert.False(RowWords.Succeeded(new RowResult("a", RowOutcome.Done, null, ReadBackState.Reverted, 0, 0)));
        Assert.False(RowWords.Succeeded(new RowResult("a", RowOutcome.Done, null, ReadBackState.WrittenButIgnored, 0, 0)));
        Assert.False(RowWords.Succeeded(new RowResult("a", RowOutcome.Failed, "x", null, 0, 0)));
        Assert.False(RowWords.Succeeded(new RowResult("a", RowOutcome.Skipped, "x", null, 0, 0)));
    }
}
