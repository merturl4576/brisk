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
    public void SensorsAnswering_NeedNoNotice() =>
        Assert.Null(Program.SensorNotice(new Sensors { Cpu = 51 }, elevated: false));

    [Fact]
    public void GpuOnly_StillCounts() =>
        Assert.Null(Program.SensorNotice(new Sensors { Gpu = 44 }, elevated: false));

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
