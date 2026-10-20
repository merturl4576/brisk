using System.Windows;
using Brisk.Ui.Shell;
using Size = System.Windows.Size;

namespace Brisk.Tests.Ui;

public sealed class FirstWindowSizeTests
{
    private static readonly Size Canvas = new(1100, 680);
    private static readonly Thickness Frame = new(8, 31, 8, 8);

    [Fact]
    public void ABigScreen_ShowsTheCanvasAtItsOwnSize()
    {
        var r = FirstWindowSize.For(Canvas, Frame, new Rect(0, 0, 2560, 1392));
        Assert.Equal(1116, r.Width, 3);
        Assert.Equal(719, r.Height, 3);
        Assert.Equal((2560 - 1116) / 2.0, r.Left, 3);
        Assert.Equal((1392 - 719) / 2.0, r.Top, 3);
    }

    [Fact]
    public void A1080pLaptopAt150Percent_FitsTheWorkArea_KeepingTheShape()
    {
        var work = new Rect(0, 0, 1280, 672);   // 1920x1080 at 150 %, taskbar 48
        var r = FirstWindowSize.For(Canvas, Frame, work);
        Assert.True(r.Height <= work.Height + 0.001, $"height {r.Height}");
        Assert.True(r.Width <= work.Width + 0.001, $"width {r.Width}");
        Assert.True(r.Top >= work.Top && r.Left >= work.Left);
        var client = new Size(r.Width - 16, r.Height - 39);
        Assert.Equal(Canvas.Width / Canvas.Height, client.Width / client.Height, 2);
    }

    [Fact]
    public void AWorkAreaThatDoesNotStartAtZero_IsRespected()
    {
        var r = FirstWindowSize.For(Canvas, Frame, new Rect(1920, 40, 1280, 672));
        Assert.True(r.Left >= 1920 && r.Top >= 40);
    }
}
