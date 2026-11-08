using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Brisk.Tests.Snapshots;
using Brisk.Ui;
using Brisk.Ui.Controls;
using Brisk.Ui.Shell;
using Xunit.Abstractions;

namespace Brisk.Tests.Ui;

/// The spec's motion budget, measured: Home shown with the moving background
/// on must average at most 3 % of the whole machine's CPU over ten seconds,
/// and a minimized window must stop the ambient clocks.
public sealed class MotionBudgetTests
{
    private readonly ITestOutputHelper _out;

    public MotionBudgetTests(ITestOutputHelper output) => _out = output;

    private static void Pump(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = span };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            frame.Continue = false;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static double CpuPercent(Action wait, TimeSpan span)
    {
        var p = Process.GetCurrentProcess();
        p.Refresh();
        var before = p.TotalProcessorTime;
        wait();
        p.Refresh();
        return (p.TotalProcessorTime - before).TotalMilliseconds / (span.TotalMilliseconds * Environment.ProcessorCount) * 100;
    }

    /// The MAX tile's fire ticks at most 20 times a second, only while the
    /// ambient clock runs, and leaves no timer behind when it stops.
    [Fact]
    public void PixelFire_draws_at_most_twenty_frames_a_second()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var timers = new ManualTimers();
            var clock = new AmbientClock(() => true);
            clock.SetVisible(true);
            var fire = new PixelFire(clock, timers, new Random(1));
            Assert.True(fire.Running);
            Assert.True(PixelFire.FrameInterval >= TimeSpan.FromMilliseconds(50));

            var before = fire.Frames;
            timers.Advance(TimeSpan.FromSeconds(1));
            Assert.InRange(fire.Frames - before, 1, 20);

            clock.SetVisible(false);
            Assert.False(fire.Running);
            Assert.Equal(0, timers.Live);
        });
    }

    [ManualFact]
    public void Home_WithMotion_StaysUnderThreePercent()
    {
        double visible = 0, minimized = 0;
        bool clockRanVisible = false, clockRanMinimized = true;
        SnapshotRenderer.OnUiThread(() =>
        {
            var clock = new AmbientClock(() => (System.Windows.Media.RenderCapability.Tier >> 16) >= 2);
            AmbientClock.Install(clock);
            var shell = NewGuiSnapshotTests.Shell(NewGuiSnapshotTests.Catalog(), clock: clock);
            var window = new ShellWindow(shell) { Left = 0, Top = 0, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show();
            // Warm-up is not idle: JIT, bitmap caches and textures settle in
            // the first seconds (measured: 78 % of a core falling to ~15 %).
            Pump(TimeSpan.FromSeconds(8));
            var span = TimeSpan.FromSeconds(10);
            visible = CpuPercent(() => Pump(span), span);
            clockRanVisible = clock.IsActive;
            window.WindowState = WindowState.Minimized;
            Pump(TimeSpan.FromSeconds(1));
            minimized = CpuPercent(() => Pump(span), span);
            clockRanMinimized = clock.IsActive;
            window.Close();
            AmbientClock.Install(new AmbientClock(() => true));
        });
        _out.WriteLine($"render tier {System.Windows.Media.RenderCapability.Tier >> 16}; cores {Environment.ProcessorCount}");
        _out.WriteLine($"visible: {visible:0.00} % of the machine; minimized: {minimized:0.00} %");
        Assert.True(clockRanVisible, "the ambient clock did not run while Home was visible");
        Assert.False(clockRanMinimized, "the ambient clock kept running while minimized");
        Assert.True(visible <= 3, $"Home with motion used {visible:0.00} % of the machine (budget 3 %)");
    }
}
