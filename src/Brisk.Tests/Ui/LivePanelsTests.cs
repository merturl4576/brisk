using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Ui.Live;

namespace Brisk.Tests.Ui;

public sealed class LivePanelsTests
{
    private static readonly object Gate = new();
    private static readonly Action<Action> Post = a => { lock (Gate) a(); };
    private static readonly Action<Action> Now = a => a();

    private static Loc Tr()
    {
        var l = new Loc();
        l.SetLanguage("tr");
        return l;
    }

    private static AppLoad App(string key, string name, double cpu, long ram, int pid, bool system = false,
        string? noteKey = null, string? note = null) =>
        new(key, name, cpu, ram, system, noteKey, note, new[] { pid }, !system);

    private static async Task<(FakeBackend B, ActionSession S, ManualTimers T, LoadPanelViewModel P)> Load(
        Action<FakeBackend> setup)
    {
        var b = FakeBackend.Typical();
        setup(b);
        var s = new ActionSession(b, Post);
        await s.LookAsync();
        var t = new ManualTimers();
        var p = new LoadPanelViewModel(Post, Now, b, s, t, Tr());
        return (b, s, t, p);
    }

    [Fact]
    public async Task Close_OffersForceOnlyIfStillRunningAfterFiveSeconds()
    {
        var (b, _, t, p) = await Load(x => x.Load.Add(App("chrome", "Google Chrome", 40, 2L << 30, 11)));
        p.Activate();
        var row = Assert.Single(p.Rows);
        Assert.True(row.CanClose);
        b.Alive.Add(11);
        row.Close.Execute(null);
        Assert.Equal(new[] { 11 }, b.Closed);
        t.Advance(TimeSpan.FromSeconds(4));
        Assert.False(row.ShowForce);
        t.Advance(TimeSpan.FromSeconds(2));
        Assert.True(row.ShowForce);
    }

    [Fact]
    public async Task Close_AppThatClosed_NeverOffersForce()
    {
        var (b, _, t, p) = await Load(x => x.Load.Add(App("chrome", "Google Chrome", 40, 2L << 30, 11)));
        p.Activate();
        p.Rows[0].Close.Execute(null);
        t.Advance(TimeSpan.FromSeconds(6));
        Assert.False(p.Rows[0].ShowForce);
    }

    [Fact]
    public async Task Force_KillsAndResamples()
    {
        var (b, _, t, p) = await Load(x => x.Load.Add(App("chrome", "Google Chrome", 40, 2L << 30, 11)));
        p.Activate();
        b.Alive.Add(11);
        p.Rows[0].Close.Execute(null);
        t.Advance(TimeSpan.FromSeconds(6));
        var samples = b.LoadSamples;
        p.Rows[0].Force.Execute(null);
        Assert.Equal(new[] { 11 }, b.Killed);
        Assert.Equal(samples + 1, b.LoadSamples);
    }

    [Fact]
    public async Task SystemRows_HaveNoButtons_AndShowTheirNote()
    {
        var (_, _, _, p) = await Load(x => x.Load.Add(App("MsMpEng", "MsMpEng", 30, 300L << 20, 5, system: true,
            noteKey: "action.load.note.defender", note: "Defender is scanning")));
        p.Activate();
        var row = Assert.Single(p.Rows);
        Assert.False(row.CanClose);
        Assert.Equal("Defender tarama yapıyor", row.Note);
        Assert.Equal("%30 CPU", row.Value);
    }

    [Fact]
    public async Task StartupBox_TicksTheSessionRow()
    {
        var (_, s, _, p) = await Load(x =>
        {
            x.Load.Add(App("Spotify", "Spotify", 1, 700L << 20, 7));
            x.Startup.Add(new StartupEntry("HKCU", "Spotify", true, false, @"C:\Spotify\Spotify.exe /minimized"));
        });
        p.Activate();
        var row = Assert.Single(p.Rows);
        Assert.Equal("startup:HKCU:Spotify", row.StartupRowId);
        Assert.True(row.StartupTicked);
        row.StartupTicked = false;
        Assert.False(s.IsTicked("startup:HKCU:Spotify"));
    }

    [Fact]
    public async Task Load_Empty_SaysNothingIsSlowingYou()
    {
        var (_, _, _, p) = await Load(_ => { });
        p.Activate();
        Assert.True(p.Empty);
        Assert.False(p.Measuring);
    }

    [Fact]
    public async Task Load_SampledEveryFifteenSecondsWhileActive()
    {
        var (b, _, t, p) = await Load(_ => { });
        p.Activate();
        t.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(3, b.LoadSamples);
        p.Deactivate();
        t.Advance(TimeSpan.FromSeconds(60));
        Assert.Equal(3, b.LoadSamples);
    }

    private static (FakeBackend B, ManualTimers T, ConnectionsViewModel C) Connections(Action<FakeBackend> setup)
    {
        var b = new FakeBackend();
        setup(b);
        var t = new ManualTimers();
        return (b, t, new ConnectionsViewModel(Post, Now, b, t, Tr()));
    }

    [Fact]
    public void Connections_RefreshEveryTwoSecondsOnlyWhileActive()
    {
        var (b, t, c) = Connections(_ => { });
        c.Activate();
        t.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(3, b.ConnectionReads);
        c.Deactivate();
        t.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, b.ConnectionReads);
    }

    [Fact]
    public void Connections_TelemetryRowsAreFlagged_AndLocalIsNamed()
    {
        var (_, _, c) = Connections(b => b.Connections = new ConnectionSnapshot(new[]
        {
            new ConnectionRow("Windows", "Microsoft", "v10.events.data.microsoft.com", 2, true, false),
            new ConnectionRow("Spotify", "Local network", null, 1, false, true),
        }, true));
        c.Activate();
        Assert.Equal(new ConnectionLine("Windows → Microsoft (v10.events.data.microsoft.com) · 2", true), c.Rows[0]);
        Assert.Equal(new ConnectionLine("Spotify → Yerel ağ · 1", false), c.Rows[1]);
        Assert.True(c.DnsRead);
    }

    [Fact]
    public void Connections_ReaderThrows_ShowsUnread()
    {
        var (_, _, c) = Connections(b => b.FailConnections = new InvalidOperationException("x"));
        c.Activate();
        Assert.Equal("Okunamadı.", c.Error);
    }

    [Fact]
    public void Usage_IsReadOnce_AcrossActivations()
    {
        var b = new FakeBackend { Usage = new[] { new UsageRow("Google Chrome", 3L << 30, 40L << 30) } };
        var u = new UsageViewModel(Post, Now, b, Tr());
        u.Activate();
        u.Activate();
        Assert.Equal(1, b.UsageReads);
        Assert.Equal(new UsageLine("Google Chrome", "gönderilen 3,0 GB", "alınan 40 GB"), u.Rows.Single());
    }

    [Fact]
    public void Usage_Null_ShowsUnread()
    {
        var b = new FakeBackend { Usage = null };
        var u = new UsageViewModel(Post, Now, b, Tr());
        u.Activate();
        Assert.Equal("Okunamadı.", u.Error);
    }
}
