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
        // The first show sizes the client area to the design canvas exactly;
        // after that the window is the user's to resize and the canvas scales.
        ContentRendered += (_, _) =>
        {
            if (SizeToContent == SizeToContent.Manual) return;
            SizeToContent = SizeToContent.Manual;
            Root.Width = double.NaN;
            Root.Height = double.NaN;
        };
        // Live readers and the moving background stop when nobody can see them.
        IsVisibleChanged += (_, _) => UpdateVisible();
        StateChanged += (_, _) => UpdateVisible();
    }

    public ShellViewModel ViewModel => _vm;

    private void UpdateVisible() => _vm.SetWindowVisible(IsVisible && WindowState != WindowState.Minimized);
}
