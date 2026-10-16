using System.Windows;
using System.Windows.Media;
using Brisk.Tests.Snapshots;
using Color = System.Windows.Media.Color;

namespace Brisk.Tests.Ui;

public sealed class MonsterThemeTests
{
    [Fact]
    public void Theme_Loads_WithTheSpecPalette()
    {
        SnapshotRenderer.OnUiThread(() =>
        {
            var d = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/brisk-app;component/Ui/Theme/Monster.xaml"),
            };
            Assert.Equal(Color.FromRgb(0x5F, 0xD4, 0xE8), ((SolidColorBrush)d["Ui.Accent"]).Color);
            Assert.Equal(Color.FromRgb(0x3E, 0xCF, 0x8E), ((SolidColorBrush)d["Ui.Green"]).Color);
            foreach (var key in new[] { "Ui.Primary", "Ui.Outline", "Ui.Link", "Ui.Check", "Ui.Switch", "Ui.Tile", "Ui.Scroll", "Ui.Background" })
                Assert.True(d.Contains(key), key);
        });
    }
}
