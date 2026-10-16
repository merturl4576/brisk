using System.IO;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;

namespace Brisk.Tests.Session;

public sealed class HomeFiguresTests
{
    private static readonly object Gate = new();
    private static readonly DateTime Now = DateTime.UtcNow;

    private static Loc Tr() { var l = new Loc(); l.SetLanguage("tr"); return l; }
    private static Loc En() { var l = new Loc(); l.SetLanguage("en"); return l; }

    private static async Task<ActionSession> Ready(FakeBackend b)
    {
        var s = new ActionSession(b, a => { lock (Gate) a(); });
        await s.LookAsync();
        return s;
    }

    [Fact]
    public async Task Ready_HeroIsTickedSpace_GaugeIsTickedOverFound()
    {
        var s = await Ready(FakeBackend.Typical());
        var f = HomeFigures.From(s, Tr(), 14, 22, Now);
        Assert.Equal("Açılabilir yer", f.HeroLabel);
        Assert.Equal(14_000_000_300, f.HeroBytes);
        Assert.Equal(14_000_000_300 / 16_000_000_300.0, f.GaugeFraction, 6);
        Assert.False(f.GaugeGreen);
        Assert.Equal("14 → 13", f.Speed.Value);
        Assert.Equal("1 ayar", f.Privacy.Value);
        Assert.Equal("13 GB", f.Space.Value);
        Assert.Equal("15 GB", f.Found);
        Assert.Equal("14", f.Startup);
        Assert.Equal("22", f.Online);
        Assert.Equal("Hazır · 6 iş seçili", f.Stateline);
        Assert.Equal(HomeButton.RunAll, f.ButtonAction);
        Assert.Equal("Hepsini yap", f.Button);
        Assert.True(f.ButtonEnabled);
        Assert.Null(f.Previous);
    }

    [Fact]
    public async Task Ready_NothingTicked_DisablesTheButton()
    {
        var s = await Ready(FakeBackend.Typical());
        foreach (var i in s.Items.ToList()) s.SetTicked(i.Id, false);
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.False(f.ButtonEnabled);
        Assert.Equal("Yapacak bir şey yok ✓", f.Stateline);
        Assert.Equal("—", f.Online);
    }

    [Fact]
    public async Task Done_ShowsTheReceiptsRealFreedSpace_AndUndo()
    {
        var b = FakeBackend.Typical();
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 9_000_000_000, startupAfter: 13, privacyOff: 1);
        var s = await Ready(b);
        await s.RunAsync(null);
        var f = HomeFigures.From(s, Tr(), 13, null, Now);
        Assert.Equal("Açıldı", f.HeroLabel);
        Assert.Equal(9_000_000_000, f.HeroBytes);
        Assert.True(f.GaugeGreen);
        Assert.True(f.Space.Done && f.Speed.Done && f.Privacy.Done);
        Assert.Equal("✓ 13 kaldı", f.Speed.Value);
        Assert.Equal("✓ 1 kapandı", f.Privacy.Value);
        Assert.Equal("✓ 8,4 GB", f.Space.Value);
        Assert.Equal("8,4 GB açıldı · 1 program açılıştan çıktı · 1 ayar kapandı", f.Stateline);
        Assert.Equal(Tone.Good, f.StatelineTone);
        Assert.Equal(HomeButton.Undo, f.ButtonAction);
        Assert.Equal("Geri al", f.Button);
        Assert.True(f.ShowRescanLink);
    }

    [Fact]
    public async Task Done_WithNothingUndoable_OffersRescanNotUndo()
    {
        var s = await Ready(FakeBackend.Typical());
        await s.RunAsync(ActionGroupKind.Space, skipUnrecoverable: true);
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Equal(HomeButton.Rescan, f.ButtonAction);
        Assert.Equal("Tekrar tara", f.Button);
        Assert.False(f.ShowRescanLink);
        Assert.False(f.Speed.Done);              // speed was not in this run
    }

    [Fact]
    public async Task Done_FailedRows_AddAnAmberPart()
    {
        var b = FakeBackend.Typical();
        b.Outcome = i => i.Id == "privacy.ads"
            ? new RowResult(i.Id, RowOutcome.Failed, "x", null, 0, 0)
            : new RowResult(i.Id, RowOutcome.Done, null, null, i.Bytes ?? 0, 0);
        var s = await Ready(b);
        await s.RunAsync(null);
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Contains("1 iş olmadı", f.Stateline);
        Assert.Equal(Tone.Warn, f.StatelineTone);
    }

    [Fact]
    public async Task Done_RestartRows_SayRestart_AndDeferredSaysWhen()
    {
        var b = FakeBackend.Typical();
        b.Outcome = i => i.Id == "speed.animations"
            ? new RowResult(i.Id, RowOutcome.PendingRestart, null, null, 0, 0)
            : new RowResult(i.Id, RowOutcome.Done, null, null, i.Bytes ?? 0, 0);
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 1L << 30) with { DeferredBytes = 2L << 30 };
        var s = await Ready(b);
        await s.RunAsync(null);
        var f = HomeFigures.From(s, Tr(), 14, null, Now, new[] { "chrome" });
        Assert.Contains("2,0 GB daha, chrome kapanınca", f.Stateline);
        Assert.EndsWith("tam etki için yeniden başlat", f.Stateline);
    }

    [Fact]
    public async Task Running_HeroCountsBytesFreedSoFar_InGreen()
    {
        var b = FakeBackend.Typical(); b.HoldRun = new ManualResetEventSlim(false);
        var s = await Ready(b);
        var run = s.RunAsync(null);
        Assert.True(SpinWait.SpinUntil(() => s.State == SessionState.Running, 2000));
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Equal("Açıldı", f.HeroLabel);
        Assert.Equal(0, f.HeroBytes);
        Assert.True(f.GaugeGreen);
        Assert.False(f.ButtonEnabled);
        Assert.Equal("Yapılıyor…", f.Button);
        b.HoldRun.Set();
        await run;
    }

    [Fact]
    public void Looking_BeforeAnything_ShowsPendingSatellitesAndDisabledButton()
    {
        var s = new ActionSession(FakeBackend.Typical(), a => a());
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Equal("Bakılıyor", f.HeroLabel);
        Assert.Equal("…", f.Speed.Value);
        Assert.Equal("…", f.Privacy.Value);
        Assert.Equal("Bakılıyor…", f.Stateline);
        Assert.Equal("Bakılıyor…", f.Button);
        Assert.False(f.ButtonEnabled);
    }

    [Fact]
    public async Task Ready_WithARecentPreviousRun_LeadsWithLastTime()
    {
        var b = FakeBackend.Typical();
        b.Latest = new RunRecord("old", Now.AddDays(-1), Now.AddDays(-1), Array.Empty<RowResult>(), new[] { "privacy.ads" });
        b.ReceiptToReturn = TestReceipts.Freed("old", 28_346_784_153, bootBefore: 48_000, bootAfter: 21_000);
        var s = await Ready(b);
        var f = HomeFigures.From(s, Tr(), 3, null, Now);
        Assert.Equal("Son sefer: 26 GB açıldı · açılış 48 sn → 21 sn", f.Previous);
        Assert.True(f.ShowPreviousUndo);
    }

    [Fact]
    public async Task Ready_PreviousRunOlderThanAWeek_IsNotShown()
    {
        var b = FakeBackend.Typical();
        b.Latest = new RunRecord("old", Now.AddDays(-8), Now.AddDays(-8), Array.Empty<RowResult>(), new[] { "privacy.ads" });
        b.ReceiptToReturn = TestReceipts.Freed("old", 1_000_000_000);
        var s = await Ready(b);
        Assert.Null(HomeFigures.From(s, Tr(), 3, null, Now).Previous);
    }

    [Fact]
    public async Task AfterUndo_StatelineSaysWhatCameBack()
    {
        var s = await Ready(FakeBackend.Typical());
        await s.RunAsync(null);
        await s.UndoAsync();
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Equal("3 ayar geri alındı · silinen dosyalar geri gelmez", f.Stateline);
        Assert.Null(f.Previous);                  // the undone run is not "last time"
    }

    [Fact]
    public async Task From_Turkish_And_English_DifferOnlyInWords()
    {
        var s = await Ready(FakeBackend.Typical());
        var tr = HomeFigures.From(s, Tr(), 14, 22, Now);
        var en = HomeFigures.From(s, En(), 14, 22, Now);
        Assert.Equal("Space to free", en.HeroLabel);
        Assert.Equal("Do it all", en.Button);
        Assert.Equal("Ready · 6 tasks ticked", en.Stateline);
        Assert.Equal(tr.HeroBytes, en.HeroBytes);
        Assert.Equal(tr.GaugeFraction, en.GaugeFraction);
    }

    [Fact]
    public async Task Failed_ShowsTheErrorAndRetry()
    {
        var b = FakeBackend.Typical(); b.FailSpace = new IOException("disk gone");
        var s = await Ready(b);
        var f = HomeFigures.From(s, Tr(), 14, null, Now);
        Assert.Null(f.HeroBytes);
        Assert.Equal("Bakamadım: disk gone", f.Stateline);
        Assert.Equal(Tone.Bad, f.StatelineTone);
        Assert.Equal(HomeButton.Retry, f.ButtonAction);
        Assert.True(f.ButtonEnabled);
    }
}
