using System.IO;
using System.Linq;
using System.Windows;
using Brisk.Tests.Snapshots;
using Xunit;
using Size = System.Windows.Size;

namespace Brisk.Tests;

/// The images exist so a human can look at them. What is asserted here is
/// only what can be stated: the page laid out without throwing, and the PNG
/// is not a dead render. "Not dead" is the check that matters — the report
/// card once produced a perfectly valid 312 KB PNG whose subject, the ring,
/// was blank, and a size-only smoke test passed over it.
public class SnapshotTests
{
    [Fact]
    public void OverviewPage_LaysOutAndRendersSomething()
    {
        var path = SnapshotRenderer.Capture(
            () => new Brisk.Views.OverviewPage(),
            new Size(1100, 700),
            "overview");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"render has {colors} distinct colours — a flat fill means the page " +
            "drew nothing, which is what a dead render looks like");
    }
}
