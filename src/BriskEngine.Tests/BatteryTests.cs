using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BriskEngine.Live;
using Xunit;

namespace BriskEngine.Tests;

public sealed class BatteryTests
{
    private static readonly PowerStatus OnBattery = new(AcLine: 0, Flag: 1, Percent: 76);

    [Fact]
    public void Health_is_full_over_design()
    {
        // The maintainer's battery on 2026-11-07: 13 680 of 46 740 mWh.
        Assert.Equal(29, Battery.Health(13_680, 46_740));
        Assert.Equal(90, Battery.Health(45_000, 50_000));
        Assert.Equal(13, Battery.Health(1, 8)); // 12.5 rounds up, not to even
        // A new battery can hold more than it was designed for: not clamped.
        Assert.Equal(104, Battery.Health(52_000, 50_000));
    }

    [Fact]
    public void Health_unknown_without_design()
    {
        Assert.Null(Battery.Health(45_000, null));
        Assert.Null(Battery.Health(45_000, 0));
        Assert.Null(Battery.Health(45_000, -1));
        Assert.Null(Battery.Health(null, 50_000));
        Assert.Null(Battery.Health(0, 50_000));
        Assert.Null(Battery.Health(null, null));
    }

    [Fact]
    public void No_battery_reads_null()
    {
        var wearAsked = false;
        BatteryWear Wear() { wearAsked = true; return BatteryWear.Unknown; }

        // BatteryFlag 128 = "no system battery": a desktop.
        Assert.Null(new RealBatteryProbe(() => new PowerStatus(1, 128, 255), Wear).Read());
        // GetSystemPowerStatus failed.
        Assert.Null(new RealBatteryProbe(() => null, Wear).Read());
        // Status unknown and no charge: nothing to show.
        Assert.Null(new RealBatteryProbe(() => new PowerStatus(255, 255, 255), Wear).Read());
        // A desktop never pays for the WMI read.
        Assert.False(wearAsked);
    }

    [Fact]
    public void Charge_is_read_while_the_status_flag_is_unknown()
    {
        var probe = new RealBatteryProbe(() => new PowerStatus(1, 255, 64), () => BatteryWear.Unknown);
        Assert.Equal(new BatteryInfo(64, true, null, null), probe.Read());
    }

    [Fact]
    public void Unreadable_wear_still_reads_charge_and_power()
    {
        var probe = new RealBatteryProbe(() => OnBattery, () => BatteryWear.Unknown);
        Assert.Equal(new BatteryInfo(76, false, null, null), probe.Read());

        var throwing = new RealBatteryProbe(() => OnBattery, () => throw new InvalidOperationException("WMI"));
        Assert.Equal(new BatteryInfo(76, false, null, null), throwing.Read());
    }

    [Fact]
    public void Wear_that_hangs_does_not_block_the_read()
    {
        using var gate = new ManualResetEventSlim();
        var probe = new RealBatteryProbe(() => OnBattery,
            () => { gate.Wait(); return new BatteryWear(80, 120); },
            wait: TimeSpan.FromMilliseconds(50));
        try
        {
            var clock = Stopwatch.StartNew();
            Assert.Equal(new BatteryInfo(76, false, null, null), probe.Read());
            // The read still in flight is not waited for again.
            Assert.Equal(new BatteryInfo(76, false, null, null), probe.Read());
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
        }
        finally
        {
            gate.Set();
        }

        // Once it lands, a later read picks it up.
        var deadline = Stopwatch.StartNew();
        BatteryInfo? info;
        do
        {
            info = probe.Read();
            if (info?.HealthPercent is not null) break;
            Thread.Sleep(10);
        } while (deadline.Elapsed < TimeSpan.FromSeconds(10));
        Assert.Equal(new BatteryInfo(76, false, 80, 120), info);
    }

    [Fact]
    public void Wear_is_read_once_until_the_refresh()
    {
        var now = new DateTime(2026, 11, 7, 12, 0, 0, DateTimeKind.Utc);
        var calls = 0;
        var probe = new RealBatteryProbe(() => OnBattery,
            () => { calls++; return new BatteryWear(90, 10); }, utcNow: () => now);

        probe.Read();
        probe.Read();
        Assert.Equal(new BatteryInfo(76, false, 90, 10), probe.Read());
        Assert.Equal(1, calls);

        now += RealBatteryProbe.Refresh;
        probe.Read();
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Zero_or_sentinel_cycle_count_is_unread()
    {
        Assert.Equal(412, Battery.Cycles(412));
        // Firmware that does not count answers 0: the maintainer's battery at
        // 29 % health says 0. Showing "0 cycles" would be a false fact.
        Assert.Null(Battery.Cycles(0));
        Assert.Null(Battery.Cycles(null));
        Assert.Null(Battery.Cycles(0xFFFFFFFF));
    }

    [Fact]
    public void Wmi_rows_join_by_battery_and_sum_for_health()
    {
        var one = Battery.Join(
            new[] { ("ACPI\\PNP0C0A\\0_0", (object?)13_680u) },
            new[] { ("ACPI\\PNP0C0A\\0_0", (object?)46_740u) },
            new[] { ("ACPI\\PNP0C0A\\0_0", (object?)0u) });
        Assert.Equal(new BatteryWear(29, null), Battery.Wear(one));

        // Two batteries: health over both packs; whose cycle count to show is
        // not a question one number answers.
        var two = Battery.Join(
            new[] { ("B", (object?)30_000u), ("A", (object?)20_000u) },
            new[] { ("A", (object?)40_000u), ("B", (object?)40_000u) },
            new[] { ("A", (object?)300u), ("B", (object?)500u) });
        Assert.Equal(new BatteryWear(63, null), Battery.Wear(two));

        // Design unreadable (BatteryStaticData failed): health unknown, the
        // cycle count still read.
        var noDesign = Battery.Join(
            new[] { ("A", (object?)20_000u) }, null, new[] { ("A", (object?)250u) });
        Assert.Equal(new BatteryWear(null, 250), Battery.Wear(noDesign));

        // One battery of two without a design: health is not guessed from the other.
        var half = Battery.Join(
            new[] { ("A", (object?)20_000u), ("B", (object?)20_000u) },
            new[] { ("A", (object?)40_000u) }, null);
        Assert.Equal(BatteryWear.Unknown, Battery.Wear(half));

        Assert.Equal(BatteryWear.Unknown, Battery.Wear(Battery.Join(null, null, null)));
        // BATTERY_UNKNOWN_CAPACITY is not a capacity.
        Assert.Equal(BatteryWear.Unknown, Battery.Wear(new[] { new BatteryReading(0xFFFFFFFF, 50_000, null) }));
    }

    [Fact]
    public void Device_information_reads_design_full_and_cycles()
    {
        // BATTERY_INFORMATION: Capabilities, Technology, Reserved[3],
        // Chemistry[4], DesignedCapacity @12, FullChargedCapacity @16,
        // DefaultAlert1, DefaultAlert2, CriticalBias, CycleCount @32.
        var info = new byte[36];
        BitConverter.TryWriteBytes(info.AsSpan(12), 46_740u);
        BitConverter.TryWriteBytes(info.AsSpan(16), 13_680u);
        BitConverter.TryWriteBytes(info.AsSpan(32), 7u);
        Assert.Equal(new BatteryReading(13_680, 46_740, 7), Battery.FromInformation(info));
        Assert.Null(Battery.FromInformation(new byte[20]));
    }
}
