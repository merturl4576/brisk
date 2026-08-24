using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Brisk.Tests.Snapshots;
using Xunit;
// WinForms is on in this project, so bare Brushes and Size are ambiguous.
using Brushes = System.Windows.Media.Brushes;
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

    /// A Window is a FrameworkElement, so Capture's signature promises this
    /// works. It very nearly did not: a window that was never shown has no
    /// HWND to measure itself against, lays out to nothing, and photographs
    /// as a flat fill without throwing anything at all. That is precisely
    /// the dead render this file exists to refuse, and the whole window is
    /// what the cockpit shell gets judged on — so the promise is pinned
    /// here, on the smallest window that can hold ink.
    [Fact]
    public void Window_LaysOutAndRendersItsContent()
    {
        var path = SnapshotRenderer.Capture(
            () => new Window
            {
                Width = 320,
                Height = 180,
                Background = Brushes.Black,
                Content = new TextBlock
                {
                    Text = "brisk",
                    FontSize = 56,
                    Foreground = Brushes.White,
                    Margin = new Thickness(20),
                },
            },
            new Size(320, 180),
            "window-probe");

        Assert.True(File.Exists(path));
        var colors = SnapshotRenderer.DistinctColors(path);
        Assert.True(colors > 16,
            $"window render has {colors} distinct colours — an unshown window " +
            "lays out against an HWND it does not have, so it photographs " +
            "blank rather than failing");
    }
}
