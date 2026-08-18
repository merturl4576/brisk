using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var launcher = new StartupLauncher(runner, new FakeRegistry(), @"C:\Apps\brisk-app.exe");

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
        new StartupLauncher(runner, new FakeRegistry(), @"C:\Apps\brisk-app.exe").Apply(false);

        var (exe, args) = runner.Calls[0];
        Assert.Equal("schtasks.exe", exe);
        Assert.Contains("/Delete", args);
        Assert.Contains("/TN brisk-logon", args);
    }

    [Fact]
    public void StartupLauncher_IsOn_FollowsTheQueryExitCode()
    {
        var runner = new FakeProcessRunner();
        var launcher = new StartupLauncher(runner, new FakeRegistry(), @"C:\Apps\brisk-app.exe");

        runner.NextExitCode = 0;
        Assert.True(launcher.IsOn());

        runner.NextExitCode = 1;
        Assert.False(launcher.IsOn());
    }

    /// Models schtasks closely enough for the migration: /Query answers 0 only
    /// once a /Create has actually succeeded.
    private sealed class TaskRunner : BriskEngine.Cleaning.IProcessRunner
    {
        public List<(string Exe, string Args)> Calls { get; } = new();
        public bool CreateSucceeds { get; set; } = true;
        public bool TaskExists { get; set; }

        public (int ExitCode, string StdOut) Run(string exe, string args)
        {
            Calls.Add((exe, args));
            if (args.Contains("/Create"))
            {
                if (CreateSucceeds) TaskExists = true;
                return (CreateSucceeds ? 0 : 1, "");
            }
            if (args.Contains("/Delete")) { TaskExists = false; return (0, ""); }
            return (TaskExists ? 0 : 1, "");                     // /Query
        }
    }

    private static FakeRegistry RegistryWithLegacyValue()
    {
        var registry = new FakeRegistry();
        registry.SetString(StartupLauncher.LegacyRunKey, StartupLauncher.LegacyValueName,
            "\"C:\\Apps\\brisk-app.exe\" --tray");
        return registry;
    }

    /// FIX WAVE, Finding 7. This branch replaced HKCU\Run with a Scheduled
    /// Task but removed nothing, and the maintainer's own machine still
    /// carries the old value. A user who once asked brisk to start with
    /// Windows still means it — so the migration honours that through the
    /// mechanism that works today, then drops the value.
    [Fact]
    public void Migrate_MovesTheLegacyRunValueOntoTheTask()
    {
        var runner = new TaskRunner();
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe").Migrate();

        Assert.Contains(runner.Calls, c => c.Args.Contains("/Create"));
        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    /// The stale value is what StartupManager reads, so before the migration
    /// brisk lists ITSELF twice — once from the task and once from the dead
    /// Run value, with the identical friendly caption, and toggling the wrong
    /// one reports success while changing nothing real.
    [Fact]
    public void Migrate_LeavesBriskListedOnlyOnce()
    {
        var registry = RegistryWithLegacyValue();
        var startup = new BriskEngine.Diagnostics.StartupManager(registry, null);
        var runner = new TaskRunner { TaskExists = true };
        Assert.Contains(startup.List(), e => e.Name == "brisk");   // the second row

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe").Migrate();

        Assert.DoesNotContain(startup.List(), e => e.Name == "brisk");
    }

    /// ...but never at the cost of the autostart itself: if the task cannot be
    /// created, dropping the value would silently take away something the user
    /// chose.
    [Fact]
    public void Migrate_KeepsTheValue_WhenTheTaskCannotBeCreated()
    {
        var runner = new TaskRunner { CreateSucceeds = false };
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe").Migrate();

        Assert.NotNull(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    /// A machine that never had the old value is not touched at all — no
    /// schtasks call, and above all no autostart brisk was never asked for.
    [Fact]
    public void Migrate_WithoutTheLegacyValue_ChangesNothing()
    {
        var runner = new TaskRunner();

        new StartupLauncher(runner, new FakeRegistry(), @"C:\Apps\brisk-app.exe")
            .Migrate();

        Assert.Empty(runner.Calls);
    }

    /// Turning autostart off has to clear the old value too, or brisk would
    /// claim to have left startup while still sitting in it.
    [Fact]
    public void StartupLauncher_Off_AlsoClearsTheLegacyValue()
    {
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(new TaskRunner(), registry, @"C:\Apps\brisk-app.exe")
            .Apply(false);

        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
