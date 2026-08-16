using System.Windows.Media;
using Brisk.Theming;
using Xunit;
using Color = System.Windows.Media.Color;

namespace Brisk.Tests;

public class ThemeResolverTests
{
    [Fact]
    public void ExplicitSettings_PassThrough()
    {
        Assert.Equal("dark", ThemeResolver.Resolve("dark", () => 1));
        Assert.Equal("light", ThemeResolver.Resolve("light", () => 0));
    }

    [Fact]
    public void System_FollowsRegistryValue()
    {
        Assert.Equal("light", ThemeResolver.Resolve("system", () => 1));
        Assert.Equal("dark", ThemeResolver.Resolve("system", () => 0));
        Assert.Equal("light", ThemeResolver.Resolve("system", () => null));
    }

    [Fact]
    public void AccentFrom_ParsesDword_ForcesOpaque()
    {
        var color = ThemeResolver.AccentFrom(unchecked((int)0xC40078D4));
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), color);
    }

    [Fact]
    public void AccentFrom_Null_IsDefaultBlue()
    {
        Assert.Equal(Color.FromArgb(0xFF, 0x4C, 0xC2, 0xFF), ThemeResolver.AccentFrom(null));
    }
}
