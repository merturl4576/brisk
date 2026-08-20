using Brisk.Cli;
using BriskEngine.Diagnostics;
using Xunit;

namespace BriskEngine.Tests;

file sealed class Sensors : ISensorProbe
{
    public double? Cpu;
    public double? Gpu;
    public double? CpuTempC() => Cpu;
    public double? GpuTempC() => Gpu;
    public int GpuCount() => 0;
}

/// WAVE B, B5. The CLI has no manifest on purpose — a command-line tool that
/// raises UAC on every invocation is worse than one that cannot read a
/// temperature. But silently omitting the thermals finding made "brisk scan"
/// look like it checked and found nothing wrong, which is the same lie the
/// manifest was added to stop in the GUI.
public class SensorNoticeTests
{
    [Fact]
    public void BothSensorsAnswering_NeedNoNotice() =>
        Assert.Null(Program.SensorNotice(new Sensors { Cpu = 51, Gpu = 44 }, elevated: false));

    /// This asserted Assert.Null and was named GpuOnly_StillCounts: a GPU
    /// reading counted as the sensors having answered, so a scan on the machine
    /// brisk was built against printed nothing and looked complete while the
    /// CPU had gone unread. That is the defect the thermals rule was rewritten
    /// to stop, and it lived one file away.
    [Fact]
    public void GpuOnly_SaysTheCpuWasNotRead()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false);
        Assert.NotNull(notice);
        Assert.Contains("CPU not read", notice!);
        Assert.Contains("blocklist", notice);
        Assert.Contains("cannot confirm from here", notice);
    }

    /// The mirror says less, because brisk knows less: a blocked kernel driver
    /// is not why a GPU sensor is silent.
    [Fact]
    public void CpuOnly_SaysTheGpuWasNotRead_WithoutTheCpuReason()
    {
        var notice = Program.SensorNotice(new Sensors { Cpu = 51 }, elevated: true);
        Assert.NotNull(notice);
        Assert.Contains("GPU not read", notice!);
        Assert.DoesNotContain("blocklist", notice);
        Assert.DoesNotContain("administrator", notice);
    }

    /// Elevation is named as something that can help other sensors, never as
    /// the thing that will deliver the CPU reading — on a machine running
    /// memory integrity it delivers it at no privilege level at all.
    [Fact]
    public void Unelevated_NeverPromisesElevationDeliversTheCpuReading()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false)!;
        Assert.Contains("can help other sensors", notice);
        foreach (var promise in new[] { "will fix", "to get the reading",
                                        "needs administrator", "run elevated to read" })
            Assert.DoesNotContain(promise, notice, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unelevated_SaysWhatToDoAboutIt()
    {
        var notice = Program.SensorNotice(new Sensors(), elevated: false);
        Assert.NotNull(notice);
        Assert.Contains("administrator", notice!);
    }

    /// Already elevated and still nothing: the honest answer is that this
    /// machine has no readable sensor, not that the user should elevate again.
    [Fact]
    public void Elevated_DoesNotBlameElevation()
    {
        var notice = Program.SensorNotice(new Sensors(), elevated: true);
        Assert.NotNull(notice);
        Assert.DoesNotContain("administrator", notice!);
    }
}
