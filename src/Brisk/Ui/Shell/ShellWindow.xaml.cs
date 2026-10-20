using System;
using System.Windows;
using Brisk.Windows;

namespace Brisk.Ui.Shell;

public partial class ShellWindow : Window
{
    private readonly ShellViewModel _vm;

    public ShellWindow(ShellViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Dwm.DarkTitleBar(this, true);
            Dwm.RoundCorners(this);
        };
        // Opens showing the 1100x680 design canvas at its own size, or
        // smaller with its shape kept when the work area is smaller.
        var place = FirstWindowSize.For(new Size(1100, 680), SystemParameters.WindowNonClientFrameThickness,
            SystemParameters.WorkArea);
        Left = place.Left;
        Top = place.Top;
        Width = place.Width;
        Height = place.Height;
        // Live readers and the moving background stop when nobody can see them.
        IsVisibleChanged += (_, _) => UpdateVisible();
        StateChanged += (_, _) => UpdateVisible();
    }

    public ShellViewModel ViewModel => _vm;

    private void UpdateVisible() => _vm.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
}
