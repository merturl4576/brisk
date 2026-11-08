using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brisk.Ui.Controls;

namespace Brisk.Ui.Pages;

/// Yer aç's view. The code here is the level colour's cross-fade (600 ms
/// with brisk's moving background on, at once otherwise) and Up / Down on
/// the level tiles, which moves the focus and picks the neighbour.
public partial class SpacePage : UserControl
{
    private static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(600);

    private readonly SolidColorBrush _levelBrush;
    private SpacePageViewModel? _vm;

    public SpacePage()
    {
        InitializeComponent();
        _levelBrush = (SolidColorBrush)Resources["LevelBrush"];
        DataContextChanged += (_, _) => Attach(DataContext as SpacePageViewModel);
        Unloaded += (_, _) => Attach(null);
        Loaded += (_, _) => Attach(DataContext as SpacePageViewModel);
    }

    private void Attach(SpacePageViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;
        if (_vm is not null) _vm.PropertyChanged -= OnChanged;
        _vm = vm;
        if (vm is null) return;
        vm.PropertyChanged += OnChanged;
        Paint(animate: false);
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpacePageViewModel.LevelColor)) Paint(animate: Ambient.Moving);
    }

    private void Paint(bool animate)
    {
        if (_vm is null) return;
        var to = (Color)ColorConverter.ConvertFromString(_vm.LevelColor);
        if (animate)
        {
            _levelBrush.BeginAnimation(SolidColorBrush.ColorProperty,
                (AnimationTimeline)Ambient.Paced(new ColorAnimation(to, Fade)));
        }
        else
        {
            _levelBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _levelBrush.Color = to;
        }
    }

    /// Up / Down on a tile: the neighbour takes the focus and is picked.
    private void OnTileKey(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        var tiles = Tiles.Children.OfType<Button>().ToList();
        var at = tiles.FindIndex(t => t.IsKeyboardFocusWithin);
        if (at < 0) return;
        var next = at + (e.Key == Key.Down ? 1 : -1);
        e.Handled = true;
        if (next < 0 || next >= tiles.Count) return;
        var tile = tiles[next];
        tile.Focus();
        if (tile.Command?.CanExecute(null) == true) tile.Command.Execute(null);
    }
}
