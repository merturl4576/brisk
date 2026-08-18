using System;
using System.Collections.Generic;
using System.Management;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.RealProbes;
using BriskEngine.Diagnostics.Rules;
using Xunit;

namespace BriskEngine.Tests;

/// The rows below are the shapes Win32_PhysicalMemory actually produced on
/// Windows 11 build 26200 — the two modules in the machine this rule was
/// written against, carrying their real CIM types: Speed and
/// ConfiguredClockSpeed are UInt32, Capacity is UInt64, DeviceLocator and
/// BankLabel are strings. Because they are pasted in, every test here runs on
/// any machine, with no WMI service and whatever memory the runner happens to
/// have.
///
/// They exist because nothing else in the suite executes this mapping. A swap
/// of the two speed reads leaves the build green, every other test passing,
/// and this machine correctly silent — 3200 configured over 2933 rated is
/// 109%. On the hardware the rule exists for it is 3200/2133 = 150%: silent
/// forever, on exactly the machine it was written to help.
public class HardwareProbeTests
{
    /// Samsung M471A2K43DB1-CWE, ChannelA-DIMM0. Rated 3200, configured 2933 —
    /// this platform's ceiling, not a disabled profile.
    private static Dictionary<string, object?> RealChannelA() => new()
    {
        ["DeviceLocator"] = "ChannelA-DIMM0",
        ["BankLabel"] = "BANK 0",
        ["Manufacturer"] = "Samsung",
        ["PartNumber"] = "M471A2K43DB1-CWE    ",
        ["Speed"] = (uint)3200,
        ["ConfiguredClockSpeed"] = (uint)2933,
        ["Capacity"] = (ulong)17179869184,
    };

    /// The same row shape with the reading this rule exists for: a 3200 kit
    /// sitting on the DDR4 2133 JEDEC base, which is what a profile never
    /// switched on looks like.
    private static Dictionary<string, object?> ProfileNeverEnabled()
    {
        var row = RealChannelA();
        row["ConfiguredClockSpeed"] = (uint)2133;
        return row;
    }

    /// The row's indexer, faithfully: a property the class does not define
    /// throws ManagementException rather than coming back null, which is the
    /// behaviour the parser has to survive.
    private static Func<string, object?> Row(IReadOnlyDictionary<string, object?> fields) =>
        name => fields.TryGetValue(name, out var value)
            ? value
            : throw new ManagementException($"property not defined: {name}");

    private static DiagnosticContext ContextOf(params MemoryModule[] modules)
    {
        var hardware = new FakeHardware();
        hardware.Modules.AddRange(modules);
        return TestContext.Empty() with { Hardware = hardware };
    }

    // ---- rated and configured, the right way round -------------------------

    /// The whole point of this file. Speed is the rating; ConfiguredClockSpeed
    /// is what the controller set. Reversing them is invisible to every other
    /// test in the suite.
    [Fact]
    public void RealRow_MapsEveryFieldToTheRightField()
    {
        var module = MemoryModuleParser.Read(Row(RealChannelA()));

        Assert.Equal("ChannelA-DIMM0", module.Slot);
        Assert.Equal(3200, module.RatedMts);
        Assert.Equal(2933, module.ConfiguredMts);
        Assert.Equal(17179869184L, module.CapacityBytes);
    }

    /// Read from a captured row and carried all the way into the sentence. A
    /// swapped mapping does not merely mislabel here, it silences the rule
    /// outright — 3200 over 2133 is 150%, far above the line — so this asserts
    /// both that a finding happens at all and that the two numbers land in the
    /// order the copy claims for them.
    [Fact]
    public void ProfileNeverEnabledRow_ReachesTheRuleWithItsNumbersInOrder()
    {
        var module = MemoryModuleParser.Read(Row(ProfileNeverEnabled()));
        Assert.Equal(3200, module.RatedMts);
        Assert.Equal(2133, module.ConfiguredMts);

        var finding = new MemorySpeedRule().Detect(ContextOf(module));

        Assert.NotNull(finding);
        Assert.Contains("ChannelA-DIMM0 2133 MT/s / 3200 MT/s", finding!.Evidence);
    }

    /// The reading that shaped the threshold, end to end from the WMI rows.
    [Fact]
    public void RealRows_LeaveTheRuleSilent()
    {
        var channelB = RealChannelA();
        channelB["DeviceLocator"] = "ChannelB-DIMM0";
        channelB["BankLabel"] = "BANK 2";
        channelB["Manufacturer"] = "Crucial";
        channelB["PartNumber"] = "CT16G4SFRA32A.M16FR ";

        var modules = new[]
        {
            MemoryModuleParser.Read(Row(RealChannelA())),
            MemoryModuleParser.Read(Row(channelB)),
        };

        Assert.Equal("ChannelB-DIMM0", modules[1].Slot);
        Assert.Null(new MemorySpeedRule().Detect(ContextOf(modules)));
    }

    // ---- the shapes firmware actually produces -----------------------------

    /// Some firmware writes the numeric properties as strings. Convert handles
    /// it; an unguarded cast would not.
    [Fact]
    public void StringValuedSpeeds_AreRead()
    {
        var row = RealChannelA();
        row["Speed"] = "3200";
        row["ConfiguredClockSpeed"] = "2133";

        var module = MemoryModuleParser.Read(Row(row));

        Assert.Equal(3200, module.RatedMts);
        Assert.Equal(2133, module.ConfiguredMts);
    }

    /// Capacity arrives as UInt64 here and as a decimal string elsewhere, which
    /// is why it is read through decimal: 16 GB overflows the Int32 an
    /// unguarded Convert invites, and UInt64 does not fit Int64 in general.
    [Fact]
    public void Capacity_ReadsUInt64()
    {
        var row = RealChannelA();
        row["Capacity"] = (ulong)17179869184;
        Assert.Equal(17179869184L, MemoryModuleParser.Read(Row(row)).CapacityBytes);
    }

    [Fact]
    public void Capacity_ReadsDecimalString()
    {
        var row = RealChannelA();
        row["Capacity"] = "17179869184";
        Assert.Equal(17179869184L, MemoryModuleParser.Read(Row(row)).CapacityBytes);
    }

    /// Past Int64 there is nothing honest to report, and a wrapped negative
    /// would be worse than saying nothing.
    [Fact]
    public void Capacity_BeyondInt64_IsUnknown()
    {
        var row = RealChannelA();
        row["Capacity"] = ulong.MaxValue;
        Assert.Equal(0, MemoryModuleParser.Read(Row(row)).CapacityBytes);
    }

    // ---- missing means missing ----------------------------------------------

    /// ConfiguredClockSpeed does not exist on every Windows release. The
    /// property read throws, and the module has to come back unknown rather
    /// than configured at zero — which the rule would otherwise be free to
    /// read as memory running at no speed at all.
    [Fact]
    public void PropertyTheClassDoesNotDefine_IsUnknown_AndSilencesTheRule()
    {
        var row = RealChannelA();
        row.Remove("ConfiguredClockSpeed");

        var module = MemoryModuleParser.Read(Row(row));

        Assert.Equal(3200, module.RatedMts);      // the rest of the row survives
        Assert.Equal(0, module.ConfiguredMts);
        Assert.Null(new MemorySpeedRule().Detect(ContextOf(module)));
    }

    /// One property failing must not cost the whole module, whatever it throws.
    [Fact]
    public void PropertyIndexerThrowingAnythingElse_IsUnknown()
    {
        var fields = RealChannelA();
        Func<string, object?> row = name => name == "Speed"
            ? throw new InvalidOperationException("provider fell over")
            : fields[name];

        var module = MemoryModuleParser.Read(row);

        Assert.Equal(0, module.RatedMts);
        Assert.Equal(2933, module.ConfiguredMts);   // still read
        Assert.Equal("ChannelA-DIMM0", module.Slot);
    }

    [Fact]
    public void NullZeroAndNonsenseSpeeds_AreAllUnknown()
    {
        foreach (var nothing in new object?[] { null, (uint)0, -1, "", "not a number" })
        {
            var row = RealChannelA();
            row["Speed"] = nothing;
            Assert.Equal(0, MemoryModuleParser.Read(Row(row)).RatedMts);
        }
    }

    // ---- the slot label ------------------------------------------------------

    [Fact]
    public void MissingDeviceLocator_FallsBackToBankLabel()
    {
        var row = RealChannelA();
        row["DeviceLocator"] = "";
        Assert.Equal("BANK 0", MemoryModuleParser.Read(Row(row)).Slot);
    }

    /// Neither label filled in leaves the module unlabelled rather than
    /// carrying an invented name — and the rule's sentence must not open with
    /// a stray space where the name would have been.
    [Fact]
    public void NoLabelAtAll_LeavesTheSlotEmpty_AndTheSentenceClean()
    {
        var row = ProfileNeverEnabled();
        row.Remove("DeviceLocator");
        row.Remove("BankLabel");

        var module = MemoryModuleParser.Read(Row(row));
        Assert.Equal("", module.Slot);

        var evidence = new MemorySpeedRule().Detect(ContextOf(module))!.Evidence;
        Assert.StartsWith("2133 MT/s / 3200 MT/s", evidence);
    }

    /// WMI pads PartNumber and friends out to a fixed width; DeviceLocator can
    /// arrive the same way, and the padding has no business in the sentence.
    [Fact]
    public void PaddedSlotLabel_IsTrimmed()
    {
        var row = RealChannelA();
        row["DeviceLocator"] = "  ChannelA-DIMM0  ";
        Assert.Equal("ChannelA-DIMM0", MemoryModuleParser.Read(Row(row)).Slot);
    }

    // ---- the live entry point -------------------------------------------------

    /// The one thing pasted rows cannot cover: that the real query comes back
    /// rather than throwing. It asserts nothing about the contents, because a
    /// runner may have no WMI at all — an empty inventory is a valid answer
    /// and an exception out of a probe never is.
    [Fact]
    public void RealProbe_ReturnsWithoutThrowing()
    {
        Assert.NotNull(new RealHardwareProbe().MemoryModules());
    }
}
