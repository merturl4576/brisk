using System;
using System.IO;
using Brisk.Services;
using Xunit;

namespace Brisk.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-set-").FullName;

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var s = Settings.Load(Path.Combine(_root, "nope", "settings.json"));
        Assert.Equal("system", s.Language);
        Assert.Equal("dark", s.Theme);   // fresh install opens the cockpit dark
        Assert.False(s.DryRun);
        Assert.False(s.StartWithWindows);
    }

    [Fact]
    public void Load_FileWithoutThemeKey_DefaultsToDark()
    {
        var path = Path.Combine(_root, "no-theme.json");
        File.WriteAllText(path, """{ "Language": "tr" }""");
        var s = Settings.Load(path);
        Assert.Equal("tr", s.Language);
        Assert.Equal("dark", s.Theme);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("system")]
    public void Load_ExplicitThemeChoice_IsHonored(string theme)
    {
        var path = Path.Combine(_root, $"theme-{theme}.json");
        File.WriteAllText(path, $$"""{ "Theme": "{{theme}}" }""");
        Assert.Equal(theme, Settings.Load(path).Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var path = Path.Combine(_root, "sub", "settings.json");
        new Settings { Language = "tr", Theme = "dark", DryRun = true }.Save(path);
        var s = Settings.Load(path);
        Assert.Equal("tr", s.Language);
        Assert.Equal("dark", s.Theme);
        Assert.True(s.DryRun);
    }

    [Fact]
    public void Load_CorruptFile_GivesDefaults()
    {
        var path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, "{{{ nope");
        Assert.Equal("system", Settings.Load(path).Language);
    }

    // HKCU\Run cannot start an app that requires elevation — Windows just
    // skips it. A Scheduled Task at highest privileges is the supported way,
    // and it starts without a UAC prompt at logon.
    [Fact]
    public void StartupLauncher_OnCreatesElevatedLogonTask()
    {
        var runner = new FakeProcessRunner();
        var launcher = new StartupLauncher(runner, @"C:\Apps\brisk-app.exe");

        launcher.Apply(true);

        var (exe, args) = runner.Calls[0];
        Assert.Equal("schtasks.exe", exe);
        Assert.Contains("/Create", args);
        Assert.Contains("/TN brisk-logon", args);
        Assert.Contains("/SC ONLOGON", args);
        Assert.Contains("/RL HIGHEST", args);
        Assert.Contains(@"C:\Apps\brisk-app.exe", args);
        Assert.Contains("--tray", args);
    }

    [Fact]
    public void StartupLauncher_OffDeletesTheTask()
    {
        var runner = new FakeProcessRunner();
        new StartupLauncher(runner, @"C:\Apps\brisk-app.exe").Apply(false);

        var (exe, args) = runner.Calls[0];
        Assert.Equal("schtasks.exe", exe);
        Assert.Contains("/Delete", args);
        Assert.Contains("/TN brisk-logon", args);
    }

    [Fact]
    public void StartupLauncher_IsOn_FollowsTheQueryExitCode()
    {
        var runner = new FakeProcessRunner();
        var launcher = new StartupLauncher(runner, @"C:\Apps\brisk-app.exe");

        runner.NextExitCode = 0;
        Assert.True(launcher.IsOn());

        runner.NextExitCode = 1;
        Assert.False(launcher.IsOn());
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
