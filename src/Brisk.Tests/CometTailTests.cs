using Brisk.Views;
using Xunit;

namespace Brisk.Tests;

/// The comet's tail is four stacked arcs of decreasing opacity — these pin
/// the falloff shape and the spec bands (20–30° total, ~60% peak).
public class CometTailTests
{
    [Fact]
    public void CometSpan_StaysInsideTheSpecBand()
    {
        var total = CometTail.SegmentDegrees * CometTail.SegmentOpacities.Length;
        Assert.InRange(total, 20, 30);
    }

    [Fact]
    public void Tail_PeaksNearSixtyPercent_AndFallsOffMonotonically()
    {
        Assert.InRange(CometTail.SegmentOpacities[0], 0.55, 0.65);
        for (var i = 1; i < CometTail.SegmentOpacities.Length; i++)
            Assert.True(CometTail.SegmentOpacities[i] < CometTail.SegmentOpacities[i - 1],
                "each tail arc must be dimmer than the one ahead of it");
        // the tail's tip is a whisper, not a second head
        Assert.InRange(CometTail.SegmentOpacities[^1], 0.01, 0.15);
    }

    [Fact]
    public void Arcs_AreContiguous_TrailingBehindTheHead()
    {
        // arc 0 ends at the head's leading edge (0°) …
        Assert.Equal(0, CometTail.StartDegrees(0) + CometTail.SegmentDegrees);
        // … and each later arc starts exactly where the previous one began
        for (var i = 1; i < CometTail.SegmentOpacities.Length; i++)
            Assert.Equal(CometTail.StartDegrees(i - 1),
                CometTail.StartDegrees(i) + CometTail.SegmentDegrees);
    }
}
