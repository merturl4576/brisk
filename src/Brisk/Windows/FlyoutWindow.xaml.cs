using System;
using System.Windows;
using System.Windows.Input;
using Brisk.ViewModels;

namespace Brisk.Windows;

public partial class FlyoutWindow : Window
{
    public FlyoutWindow(FlyoutViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Deactivated += (_, _) => Hide();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Hide(); };
        SourceInitialized += (_, _) => Dwm.RoundCorners(this);
        SizeChanged += (_, _) => Position();
    }

    /// Anchors the panel to the bottom-right work-area corner, like the
    /// volume flyout. WorkArea already excludes the taskbar.
    public void ShowAt()
    {
        Show();
        Position();
        Activate();
    }

    private void Position()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 12;
        Top = area.Bottom - ActualHeight - 12;
    }
}
