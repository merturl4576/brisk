using Brisk.Theming;
using Xunit;

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

    // The two AccentFrom facts are gone with the method. They pinned a dword
    // parse and a fallback blue for a value that is no longer read: the
    // signature accent is the theme dictionary's, so there is nothing left
    // for a system accent to win. Theme DETECTION is what is still asserted
    // above, and it is the half that still runs.
}
