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
        Assert.Null(Program.SensorNotice(new Sensors { Cpu = 51, Gpu = 44 }, elevated: false,
            memoryIntegrityOn: null));

    /// This asserted Assert.Null and was named GpuOnly_StillCounts: a GPU
    /// reading counted as the sensors having answered, so a scan on the machine
    /// brisk was built against printed nothing and looked complete while the
    /// CPU had gone unread. That is the defect the thermals rule was rewritten
    /// to stop, and it lived one file away.
    [Fact]
    public void GpuOnly_SaysTheCpuWasNotRead()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false,
            memoryIntegrityOn: null);
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
        var notice = Program.SensorNotice(new Sensors { Cpu = 51 }, elevated: true,
            memoryIntegrityOn: null);
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
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false,
            memoryIntegrityOn: null)!;
        Assert.Contains("can help other sensors", notice);
        foreach (var promise in new[] { "will fix", "to get the reading",
                                        "needs administrator", "run elevated to read" })
            Assert.DoesNotContain(promise, notice, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unelevated_SaysWhatToDoAboutIt()
    {
        var notice = Program.SensorNotice(new Sensors(), elevated: false, memoryIntegrityOn: null);
        Assert.NotNull(notice);
        Assert.Contains("administrator", notice!);
    }

    /// Already elevated and still nothing: the honest answer is that this
    /// machine has no readable sensor, not that the user should elevate again.
    [Fact]
    public void Elevated_DoesNotBlameElevation()
    {
        var notice = Program.SensorNotice(new Sensors(), elevated: true, memoryIntegrityOn: null);
        Assert.NotNull(notice);
        Assert.DoesNotContain("administrator", notice!);
    }

    /// The CLI carried its own copy of the rule's one explanation, and its own
    /// copy of the hedge. Memory integrity is readable without a driver, so
    /// the claim can be checked against the machine it is made to — and on a
    /// machine with memory integrity OFF a driver Windows will not load while
    /// memory integrity is on is not why the CPU went unread.
    [Fact]
    public void GpuOnly_MemoryIntegrityOff_DropsTheBlocklistReason()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false,
            memoryIntegrityOn: false);
        Assert.NotNull(notice);
        Assert.Contains("CPU not read", notice!);
        Assert.DoesNotContain("blocklist", notice, System.StringComparison.OrdinalIgnoreCase);
    }

    /// Measured on: the sentence stops wondering whether memory integrity is
    /// on. It keeps refusing to call that the proven cause, and it still never
    /// tells anyone to switch the protection off.
    [Fact]
    public void GpuOnly_MemoryIntegrityOn_StatesItAsMeasuredWithoutOrderingItOff()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false,
            memoryIntegrityOn: true);
        Assert.NotNull(notice);
        Assert.Contains("memory integrity is on", notice!, System.StringComparison.OrdinalIgnoreCase);
        foreach (var order in new[] { "turn off", "turn it off", "switch it off",
                                      "disable", "Windows Security" })
            Assert.DoesNotContain(order, notice, System.StringComparison.OrdinalIgnoreCase);
    }

    /// Unknown keeps the hedge. A Device Guard query that failed must not be
    /// reported as memory integrity being off.
    [Fact]
    public void GpuOnly_MemoryIntegrityUnknown_KeepsTheHedge()
    {
        var notice = Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false,
            memoryIntegrityOn: null);
        Assert.NotNull(notice);
        Assert.Contains("blocklist", notice!);
        Assert.Contains("cannot confirm from here", notice);
    }
}
