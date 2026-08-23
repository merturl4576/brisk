using BriskEngine.Diagnostics.RealProbes;
using Xunit;

namespace BriskEngine.Tests;

/// The shapes a live machine cannot be made to produce on demand.
///
/// Written after the generic pattern `is IEnumerable&lt;object&gt;` shipped into
/// this probe and would have returned null on every real machine: WMI hands
/// back a uint32[], and a value-type array implements IEnumerable&lt;uint&gt;,
/// never IEnumerable&lt;object&gt;. The machine it was written on has memory
/// integrity ON, so the bug's symptom — unknown — is also exactly what a
/// working probe returns on a machine it cannot read. Nothing in the output
/// would have looked wrong.
///
/// Same reason MemoryModuleParser exists apart from RealHardwareProbe: the
/// reading is pinned without a WMI service, and the query around it stays the
/// only untested part.
public class MemoryIntegrityProbeTests
{
    /// 2 is hypervisor-enforced code integrity in Microsoft's numbering.
    [Fact]
    public void HvciInTheRunningList_IsOn() =>
        Assert.True(RealMemoryIntegrityProbe.ReadRunning(new uint[] { 2 }));

    [Fact]
    public void HvciAmongOtherServices_IsStillOn() =>
        Assert.True(RealMemoryIntegrityProbe.ReadRunning(new uint[] { 1, 2, 3 }));

    /// Other services running and not this one is a measurement, not a gap.
    [Fact]
    public void OtherServicesOnly_IsOff() =>
        Assert.False(RealMemoryIntegrityProbe.ReadRunning(new uint[] { 1, 3 }));

    /// Nothing running is also a measurement: Device Guard answered.
    [Fact]
    public void EmptyRunningList_IsOff() =>
        Assert.False(RealMemoryIntegrityProbe.ReadRunning(new uint[0]));

    /// The property is absent on editions without Device Guard. That is a
    /// machine brisk could not read, which must never be reported as off.
    [Fact]
    public void MissingProperty_IsUnknown() =>
        Assert.Null(RealMemoryIntegrityProbe.ReadRunning(null));

    /// A string is IEnumerable too, of char, and Convert.ToInt32('2') is 50 —
    /// so a scalar sneaking through would have read as a confident "off"
    /// rather than as something brisk did not understand.
    [Fact]
    public void ScalarInsteadOfList_IsUnknown() =>
        Assert.Null(RealMemoryIntegrityProbe.ReadRunning("2"));

    /// Windows changing the element type must not turn into a silent "off".
    [Fact]
    public void BoxedValues_AreStillRead() =>
        Assert.True(RealMemoryIntegrityProbe.ReadRunning(new object[] { (ushort)2 }));
}
