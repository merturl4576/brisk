using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brisk.ViewModels;
using Brisk.Views;

namespace Brisk.Services;

/// The card at 1600×900, written as a PNG at 2× (3200×1800, 192 DPI) so it
/// survives every platform's recompression. WPF is the renderer — the
/// cockpit look is inherited from the shared dictionaries, not imitated.
public static class ReportCardRenderer
{
    public const int Width = 1600;
    public const int Height = 900;

    public static void RenderToFile(ReportCardModel model, string path)
    {
        // Before the work, not after it: a relative name resolves here, a
        // missing folder is made here, and an unwritable one is refused here
        // rather than at the end of a 23 MB render. GetDirectoryName returns
        // an empty string — not null — for a bare 'card.png', and
        // CreateDirectory("") throws, so a plain --out filename used to buy
        // an ArgumentException instead of a card.
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var card = new ReportCard { DataContext = model };
        card.Measure(new Size(Width, Height));
        card.Arrange(new Rect(0, 0, Width, Height));
        card.UpdateLayout();
        SettleGauges(card);
        card.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            Width * 2, Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(card);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// The ring's ignition sweep is motion for a live window: SegmentedGauge
    /// animates LitCount up from zero whenever Score changes, and an
    /// animation clock only advances while a dispatcher is pumping frames.
    /// Offscreen there is no frame loop, so the clock never leaves zero and
    /// the lit arc comes out EMPTY — a dead grey ring on the one image whose
    /// whole subject is the score. A still frame wants the resting value, so
    /// the clock is dropped and the count is set to where the sweep would
    /// have landed. Recursive because the two layers sit inside the card's
    /// tree, not on a name the renderer could reach for.
    private static void SettleGauges(DependencyObject node)
    {
        if (node is SegmentedGauge gauge)
        {
            gauge.BeginAnimation(SegmentedGauge.LitCountProperty, null);
            gauge.LitCount = SegmentedGauge.LitCountFor(gauge.Score);
        }
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            SettleGauges(VisualTreeHelper.GetChild(node, i));
    }

    /// WPF objects demand an STA thread; the console face and the test
    /// runner do not have one. The GUI calls RenderToFile directly on the
    /// dispatcher; everyone else comes through here.
    public static void RenderOnStaThread(ReportCardModel model, string path)
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { RenderToFile(model, path); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }
}
