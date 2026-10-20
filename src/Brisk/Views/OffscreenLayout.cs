using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Brisk.Views;

/// Laying a control out with no window around it. The report card renderer
/// and the design-snapshot harness both need the same sequence, and both
/// need it for the same reason: a still frame of a live cockpit has to be
/// taken after the motion has been parked, not while it is still at zero.
public static class OffscreenLayout
{
    /// Measure, arrange, settle, and lay out once more — the second pass is
    /// what lets the settled values reach the render.
    public static void LayOut(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(new Point(0, 0), size));
        element.UpdateLayout();
        Settle(element);
        element.UpdateLayout();
    }

    /// The ring's ignition sweep is motion for a live window: SegmentedGauge
    /// animates LitCount up from zero whenever Score changes, and an
    /// animation clock only advances while a dispatcher is pumping frames.
    /// Offscreen there is no frame loop, so the clock never leaves zero and
    /// the lit arc comes out EMPTY — a dead grey ring on the one image whose
    /// whole subject is the score. A still frame wants the resting value, so
    /// the clock is dropped and the count is set to where the sweep would
    /// have landed. Recursive because the layers sit inside the tree, not on
    /// a name the renderer could reach for.
    public static void Settle(DependencyObject root)
    {
        if (root is SegmentedGauge gauge)
        {
            gauge.BeginAnimation(SegmentedGauge.LitCountProperty, null);
            gauge.LitCount = SegmentedGauge.LitCountFor(gauge.Score);
        }
        // The new window's two data-driven sweeps: the tick arc filling and
        // the hero number counting. A still frame shows where they land.
        if (root is Ui.Controls.TickGauge ticks)
        {
            ticks.BeginAnimation(Ui.Controls.TickGauge.ShownFractionProperty, null);
            ticks.ShownFraction = ticks.Fraction;
        }
        if (root is Ui.Controls.Atmosphere atmosphere) atmosphere.Settle();
        if (root is Ui.Controls.HeroNumeral hero && hero.Bytes is { } bytes)
        {
            hero.BeginAnimation(Ui.Controls.HeroNumeral.ShownBytesProperty, null);
            hero.ShownBytes = bytes;
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            Settle(VisualTreeHelper.GetChild(root, i));
    }
}
