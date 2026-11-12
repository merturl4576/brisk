using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

public sealed class HardwareSpecsTests
{
    [Fact]
    public void Specs_probe_never_throws()
    {
        // The real probe, read-only: queries only. What it finds depends on
        // the machine, so only the shape is asserted.
        var specs = new RealSpecsProbe().Read();
        Assert.NotNull(specs.Gpus);
    }

    [Fact]
    public void Cpu_sums_sockets_and_tidies_the_name()
    {
        var rows = new List<WmiRow>
        {
            new() { ["Name"] = "Intel(R) Xeon(R) CPU   E5-2690 v4 @ 2.60GHz  ", ["MaxClockSpeed"] = 2601u,
                    ["NumberOfCores"] = 14u, ["NumberOfLogicalProcessors"] = 28u },
            new() { ["Name"] = "Intel(R) Xeon(R) CPU   E5-2690 v4 @ 2.60GHz  ", ["MaxClockSpeed"] = 2601u,
                    ["NumberOfCores"] = 14u, ["NumberOfLogicalProcessors"] = 28u },
        };
        Assert.Equal(("Intel(R) Xeon(R) CPU E5-2690 v4 @ 2.60GHz", 2601, 28, 56), HardwareSpecs.Cpu(rows));

        // A socket that does not say how many cores it has: the total is unknown.
        rows[1]["NumberOfCores"] = null;
        Assert.Equal(("Intel(R) Xeon(R) CPU E5-2690 v4 @ 2.60GHz", 2601, null, 56), HardwareSpecs.Cpu(rows));

        Assert.Equal(((string?)null, (int?)null, (int?)null, (int?)null), HardwareSpecs.Cpu(null));
        Assert.Equal(((string?)null, (int?)null, (int?)null, (int?)null), HardwareSpecs.Cpu(new List<WmiRow>()));
    }

    [Fact]
    public void Gpus_skip_software_display_adapters()
    {
        var rows = new List<WmiRow>
        {
            new() { ["Name"] = "Intel(R) UHD Graphics", ["PNPDeviceID"] = @"PCI\VEN_8086&DEV_9BC4\3&11583659&0&10" },
            new() { ["Name"] = "NVIDIA GeForce GTX 1650 Ti", ["PNPDeviceID"] = @"PCI\VEN_10DE&DEV_1F95\4&A270537&0&0008" },
            new() { ["Name"] = "Microsoft Remote Display Adapter", ["PNPDeviceID"] = @"SWD\REMOTEDISPLAYENUM\RDPIDD_INDIRECTDISPLAY" },
            new() { ["Name"] = "Parsec Virtual Display Adapter", ["PNPDeviceID"] = @"ROOT\DISPLAY\0000" },
            new() { ["Name"] = "   ", ["PNPDeviceID"] = @"PCI\VEN_1234" },
            new() { ["Name"] = "Microsoft Hyper-V Video", ["PNPDeviceID"] = null },
        };
        Assert.Equal(new[] { "Intel(R) UHD Graphics", "NVIDIA GeForce GTX 1650 Ti", "Microsoft Hyper-V Video" },
            HardwareSpecs.Gpus(rows));
        Assert.Empty(HardwareSpecs.Gpus(null));
    }

    [Fact]
    public void Installed_ram_is_unknown_when_any_module_is()
    {
        var two = new List<WmiRow> { new() { ["Capacity"] = 17179869184ul }, new() { ["Capacity"] = 17179869184ul } };
        Assert.Equal(34359738368L, HardwareSpecs.RamInstalled(two));

        two[1]["Capacity"] = null;
        Assert.Null(HardwareSpecs.RamInstalled(two));
        Assert.Null(HardwareSpecs.RamInstalled(new List<WmiRow>()));
        Assert.Null(HardwareSpecs.RamInstalled(null));
    }

    [Fact]
    public void System_disk_is_the_disk_holding_the_system_letter()
    {
        var partitions = new List<WmiRow>
        {
            new() { ["DiskNumber"] = 0u, ["DriveLetter"] = '\0' },
            new() { ["DiskNumber"] = 0u, ["DriveLetter"] = 'C' },
            new() { ["DiskNumber"] = 1u, ["DriveLetter"] = 'D' },
        };
        var disks = new List<WmiRow>
        {
            new() { ["Number"] = 0u, ["Model"] = "KINGSTON SKC3000S1024G", ["FriendlyName"] = "KINGSTON SKC3000S1024G", ["Size"] = 1024209543168ul },
            new() { ["Number"] = 1u, ["Model"] = "ST2000DM008", ["FriendlyName"] = "ST2000DM008", ["Size"] = 2000398934016ul },
        };
        Assert.Equal(("KINGSTON SKC3000S1024G", 1024209543168L), HardwareSpecs.SystemDisk(partitions, disks, 'C'));
        Assert.Equal(("ST2000DM008", 2000398934016L), HardwareSpecs.SystemDisk(partitions, disks, 'd'));

        // No partition carries the letter, or the disk is missing: unknown, never the first disk.
        Assert.Equal(((string?)null, (long?)null), HardwareSpecs.SystemDisk(partitions, disks, 'E'));
        Assert.Equal(((string?)null, (long?)null), HardwareSpecs.SystemDisk(partitions, disks.GetRange(1, 1), 'C'));
        Assert.Equal(((string?)null, (long?)null), HardwareSpecs.SystemDisk(null, disks, 'C'));
        Assert.Equal(((string?)null, (long?)null), HardwareSpecs.SystemDisk(partitions, disks, null));

        // Size unreadable: the model still shows.
        disks[0]["Size"] = null;
        Assert.Equal(("KINGSTON SKC3000S1024G", (long?)null), HardwareSpecs.SystemDisk(partitions, disks, 'C'));
    }

    [Fact]
    public void A_hung_query_answers_unknown_at_the_deadline()
    {
        var never = new TaskCompletionSource<IReadOnlyList<WmiRow>?>().Task;
        var clock = Stopwatch.StartNew();
        Assert.Null(Wmi.Await(never, clock, TimeSpan.FromMilliseconds(50)));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");

        IReadOnlyList<WmiRow> rows = new List<WmiRow> { new() { ["Name"] = "x" } };
        Assert.Same(rows, Wmi.Await(Task.FromResult<IReadOnlyList<WmiRow>?>(rows), clock, TimeSpan.Zero));
    }
}
