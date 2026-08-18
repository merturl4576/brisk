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

    private const string ExePath = @"C:\Apps\brisk-app.exe";

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
        var runner = new TaskStateRunner();
        var launcher = new StartupLauncher(runner, new FakeRegistry(), ExePath);

        Assert.False(launcher.IsOn());
        launcher.Apply(true);
        Assert.True(launcher.IsOn());
        launcher.Apply(false);
        Assert.False(launcher.IsOn());
    }

    /// WAVE C, C2. Every read of the task state used to launch schtasks.exe,
    /// and one of those reads happens on the dispatcher — the Settings
    /// checkbox binds it, and that binding is evaluated inside MainWindow's
    /// constructor. One spawn, then the cache; only Apply can change the
    /// answer, so only Apply invalidates it.
    [Fact]
    public void StartupLauncher_IsOn_AsksSchtasksOnce_ThenAgainOnlyAfterApply()
    {
        var runner = new TaskStateRunner();
        var launcher = new StartupLauncher(runner, new FakeRegistry(), ExePath);

        launcher.IsOn();
        launcher.IsOn();
        launcher.IsOn();
        Assert.Single(runner.Calls.Where(c => c.Args.Contains("/Query")));

        // Apply knows what it just did, so a successful one updates the cache
        // instead of paying for another launch.
        launcher.Apply(true);
        Assert.True(launcher.IsOn());
        Assert.Single(runner.Calls.Where(c => c.Args.Contains("/Query")));
    }

    /// A refused Apply leaves the task in a state this class may no longer
    /// assume, so the cache is dropped rather than guessed at.
    [Fact]
    public void StartupLauncher_RefusedApply_DoesNotCacheAGuess()
    {
        var runner = new TaskStateRunner { TaskExists = true, DeleteSucceeds = false };
        var launcher = new StartupLauncher(runner, new FakeRegistry(), ExePath);

        Assert.False(launcher.Apply(false));

        Assert.True(launcher.IsOn());   // still there — and it went and asked
        Assert.Single(runner.Calls.Where(c => c.Args.Contains("/Query")));
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
    /// carries the old value. A user whose setting still says "start with
    /// Windows" means it — so the migration honours that through the
    /// mechanism that works today, then drops the value.
    [Fact]
    public void Migrate_MovesTheLegacyRunValueOntoTheTask()
    {
        var runner = new TaskStateRunner();
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe")
            .Migrate(autostartWanted: true);

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
        var runner = new TaskStateRunner { TaskExists = true };
        Assert.Contains(startup.List(), e => e.Name == "brisk");   // the second row

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe")
            .Migrate(autostartWanted: true);

        Assert.DoesNotContain(startup.List(), e => e.Name == "brisk");
    }

    /// FIX WAVE re-review, N2. The value is evidence of an OLD intent only. A
    /// user who upgraded to the task-based build and then explicitly turned
    /// autostart OFF in Settings has exactly this machine state — value
    /// present, no task — and treating the value as consent there lets the
    /// oldest implicit choice beat the newest explicit one, silently, at
    /// startup before any window exists to notice it.
    [Fact]
    public void Migrate_DoesNotPutBriskBackIntoStartup_AgainstANewerChoice()
    {
        var runner = new TaskStateRunner();                  // no task on this machine
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe")
            .Migrate(autostartWanted: false);

        Assert.DoesNotContain(runner.Calls, c => c.Args.Contains("/Create"));
        // ...and the dead value goes anyway: that half depends on nothing.
        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    /// Removing the value does not depend on the task either. It is an
    /// autostart Windows skips regardless (brisk requires elevation), so
    /// keeping it after a failed schtasks preserves nothing real while
    /// guaranteeing the duplicate "brisk" row.
    [Fact]
    public void Migrate_RemovesTheValue_EvenWhenTheTaskCannotBeCreated()
    {
        var runner = new TaskStateRunner { CreateSucceeds = false };
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(runner, registry, @"C:\Apps\brisk-app.exe")
            .Migrate(autostartWanted: true);

        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    /// A machine that never had the old value is not touched at all — no
    /// schtasks call, and above all no autostart brisk was never asked for.
    [Fact]
    public void Migrate_WithoutTheLegacyValue_ChangesNothing()
    {
        var runner = new TaskStateRunner();

        new StartupLauncher(runner, new FakeRegistry(), @"C:\Apps\brisk-app.exe")
            .Migrate(autostartWanted: true);

        Assert.Empty(runner.Calls);
    }

    /// Turning autostart off has to clear the old value too, or brisk would
    /// claim to have left startup while still sitting in it.
    [Fact]
    public void StartupLauncher_Off_AlsoClearsTheLegacyValue()
    {
        var registry = RegistryWithLegacyValue();

        new StartupLauncher(new TaskStateRunner(), registry, @"C:\Apps\brisk-app.exe")
            .Apply(false);

        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
    }

    /// The Run value has a companion record in Explorer's StartupApproved
    /// table holding its enabled/disabled bit. Removing the value while
    /// leaving that behind keeps dead data describing a startup entry that no
    /// longer exists — and it would decide the toggle state of any future
    /// "brisk" Run value before the user ever saw it.
    [Fact]
    public void StartupLauncher_Off_AlsoClearsTheOrphanedApprovalRecord()
    {
        var registry = RegistryWithLegacyValue();
        registry.SetBytes(StartupLauncher.LegacyApprovedKey, StartupLauncher.LegacyValueName,
            new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        new StartupLauncher(new TaskStateRunner(), registry, @"C:\Apps\brisk-app.exe")
            .Apply(false);

        Assert.Null(registry.GetString(StartupLauncher.LegacyRunKey,
            StartupLauncher.LegacyValueName));
        Assert.Null(registry.GetBytes(StartupLauncher.LegacyApprovedKey,
            StartupLauncher.LegacyValueName));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
