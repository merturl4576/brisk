using System.IO;
using System.Windows;
using System.Windows.Controls;
using BriskEngine.Actions;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.Tests.Session;
using Brisk.Tests.Ui;
using Brisk.Ui.Controls;
using Brisk.Ui.Home;
using Size = System.Windows.Size;

namespace Brisk.Tests.Snapshots;

/// Photographs of the new window, for my eyes, in .snapshots/new-*.png.
/// What the tests assert is that each state renders and binds cleanly.
public sealed class NewGuiSnapshotTests
{
    private static readonly Size Canvas = new(1100, 680);

    internal static FakeBackend Rich()
    {
        var b = FakeBackend.Typical();
        b.Load.AddRange(new[]
        {
            new AppLoad("chrome", "Google Chrome", 34, 2L << 30, false, null, null, new[] { 1 }, true),
            new AppLoad("claude", "Claude", 2, 1_500L << 20, false, null, null, new[] { 2 }, true),
            new AppLoad("discord", "Discord", 1, 900L << 20, false, null, null, new[] { 3 }, true),
        });
        b.Connections = new ConnectionSnapshot(new[]
        {
            new ConnectionRow("Google Chrome", "Google", "www.google.com", 6, false, false),
            new ConnectionRow("Discord", "Cloudflare", null, 2, false, false),
            new ConnectionRow("Windows", "Microsoft", "v10.events.data.microsoft.com", 1, true, false),
        }, true);
        return b;
    }

    internal static (HomeViewModel Vm, ActionSession Session) Home(FakeBackend b, string language = "tr")
    {
        var loc = new Loc();
        loc.SetLanguage(language);
        var gate = new object();
        Action<Action> post = a => { lock (gate) a(); };
        var session = new ActionSession(b, post);
        var vm = new HomeViewModel(post, a => a(), session, b, new ManualTimers(), loc, (_, _) => { }, _ => { });
        session.LookAsync().GetAwaiter().GetResult();
        vm.Activate();
        return (vm, session);
    }

    internal static HomeViewModel AfterRun(FakeBackend b)
    {
        var (vm, session) = Home(b);
        session.RunAsync(null).GetAwaiter().GetResult();
        return vm;
    }

    private static FrameworkElement Frame(object vm) => new Grid
    {
        Width = Canvas.Width,
        Height = Canvas.Height,
        Children = { new Atmosphere(new Brisk.Ui.AmbientClock(() => false)), new HomeView { DataContext = vm } },
    };

    private static void Shoot(Func<object> vm, string name)
    {
        var errors = BindingErrors.Capture(() =>
            SnapshotRenderer.Capture(() => Frame(vm()), Canvas, name));
        Assert.Empty(errors);
        Assert.True(SnapshotRenderer.DistinctColors(Path.Combine(SnapshotDirFor(name))) > 50);
    }

    private static string SnapshotDirFor(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "brisk.sln")))
                return Path.Combine(dir.FullName, ".snapshots", name + ".png");
        throw new InvalidOperationException();
    }

    [Fact]
    public void Home_Ready() => Shoot(() => Home(Rich()).Vm, "new-home-ready");

    [Fact]
    public void Home_Ready_English() => Shoot(() => Home(Rich(), "en").Vm, "new-home-ready-en");

    [Fact]
    public void Home_Running()
    {
        var b = Rich();
        b.HoldRun = new ManualResetEventSlim(false);
        try
        {
            Shoot(() =>
            {
                var (vm, session) = Home(b);
                _ = session.RunAsync(null, skipUnrecoverable: true);
                SpinWait.SpinUntil(() => session.State == SessionState.Running, 2000);
                return vm;
            }, "new-home-running");
        }
        finally { b.HoldRun.Set(); }
    }

    [Fact]
    public void Home_Done()
    {
        var b = Rich();
        b.ReceiptToReturn = TestReceipts.Freed("run-1", 28_346_784_153, startupAfter: 3, privacyOff: 1);
        Shoot(() => AfterRun(b), "new-home-done");
    }
}
