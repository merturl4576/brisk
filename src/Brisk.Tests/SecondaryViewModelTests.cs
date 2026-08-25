using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using BriskEngine.Diagnostics;
using Xunit;

namespace Brisk.Tests;

public sealed class SecondaryViewModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("brisk-vm2-").FullName;

    private static Brisk.Localization.Loc EnglishLoc()
    {
        var loc = new Brisk.Localization.Loc();
        loc.SetLanguage("en");
        return loc;
    }

    // Autostart off (schtasks /Query exits non-zero) — these existing tests
    // predate brisk's own startup row and don't want it in the mix.
    private static StartupLauncher OffLauncher() =>
        new(new FakeProcessRunner { NextExitCode = 1 }, new FakeRegistry(), @"C:\x\brisk-app.exe");

    [Fact]
    public async Task Startup_ListsHeavyFirst_TogglesThroughHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "Discord", true, true));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            OffLauncher());
        await state.ScanAsync();

        Assert.Equal(new[] { "Discord", "MyTool" },
            vm.Items.Select(i => i.Name).ToArray());

        vm.Items[0].IsEnabled = false;
        Assert.Equal(("HKCU", "Discord", false), Assert.Single(host.StartupToggles));
        Assert.False(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_FailedToggle_RevertsAndFlags()
    {
        var host = new FakeEngineHost { StartupToggleResult = false };
        host.Startup.Add(new StartupEntry("HKLM", "Svc", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            OffLauncher());
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    [Fact]
    public async Task Startup_DryRun_TogglesLikeFailedToggle_NeverCallsHost()
    {
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => true,
            OffLauncher());
        await state.ScanAsync();

        vm.Items[0].IsEnabled = false;

        Assert.Empty(host.StartupToggles);
        Assert.True(vm.Items[0].IsEnabled);   // reverted
        Assert.True(vm.ToggleFailed);
    }

    /// WAVE C, C2. B3 gave the two surfaces one backing truth, and the symptom
    /// survived it: SettingsPage is built once and WPF caches a bound value
    /// until something raises PropertyChanged, so turning brisk off on the
    /// Startup page left the checkbox reading "on" for the life of the
    /// process. It self-healed only if the user clicked the stale checkbox,
    /// which is a confusing repair.
    [Fact]
    public async Task StartupPageTurningBriskOff_ReachesTheSettingsCheckbox()
    {
        var runner = new TaskStateRunner { TaskExists = true };
        var launcher = new StartupLauncher(runner, new FakeRegistry(),
            @"C:\x\brisk-app.exe");
        var settings = new Settings { StartWithWindows = true };
        var settingsVm = new SettingsViewModel(settings,
            Path.Combine(_root, "cross.json"), launcher, _ => { }, _ => { });
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var startupVm = new StartupViewModel(state, host, EnglishLoc(),
            () => false, launcher);
        await state.ScanAsync();

        var raised = new List<string>();
        settingsVm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        startupVm.Items.Single(i => i.Hive == StartupItemRow.TaskHive).IsEnabled = false;

        Assert.False(settingsVm.StartWithWindows);
        Assert.Contains(nameof(SettingsViewModel.StartWithWindows), raised);
        // ...and the stored record stops contradicting the machine (the
        // drift the coordinator listed as out of scope: it was two lines
        // once the notification existed, so it is closed here).
        Assert.False(Settings.Load(Path.Combine(_root, "cross.json")).StartWithWindows);
    }

    /// The other direction: the Settings page owns the same toggle, so the
    /// Startup page's own row must not keep its old reading either.
    [Fact]
    public async Task SettingsTurningBriskOn_ReachesTheStartupPage()
    {
        var launcher = new StartupLauncher(new TaskStateRunner(), new FakeRegistry(),
            @"C:\x\brisk-app.exe");
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var startupVm = new StartupViewModel(state, host, EnglishLoc(),
            () => false, launcher);
        var settingsVm = new SettingsViewModel(new Settings(),
            Path.Combine(_root, "cross2.json"), launcher, _ => { }, _ => { });
        await state.ScanAsync();
        Assert.DoesNotContain(startupVm.Items, i => i.Hive == StartupItemRow.TaskHive);

        settingsVm.StartWithWindows = true;

        Assert.Contains(startupVm.Items, i => i.Hive == StartupItemRow.TaskHive);
    }

    /// WAVE B, B4. "brisk" was a CONTAINS-match in the known-apps table, so
    /// BriskBard — a real browser — would have been described to its owner as
    /// "brisk itself, turn this off to stop it starting with Windows": a false
    /// statement about their machine, inside the feature built to prove brisk
    /// is honest about itself. brisk's own row is the synthetic one and is
    /// recognised by its Task hive.
    [Fact]
    public async Task Startup_DoesNotClaimSomeoneElsesProgramIsBrisk()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "BriskBard", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, loc, () => false,
            new StartupLauncher(new TaskStateRunner { TaskExists = true },
                new FakeRegistry(), @"C:\x\brisk-app.exe"));
        await state.ScanAsync();

        Assert.Equal("", vm.Items.Single(i => i.Name == "BriskBard").Description);
        // ...while brisk's own row, which comes from the task, still says so.
        Assert.Equal(loc["startup.app.brisk"],
            vm.Items.Single(i => i.Hive == StartupItemRow.TaskHive).Description);
    }

    /// WAVE B, B3. StartupViewModel hardcoded ToggleFailed = false after
    /// Apply, which made brisk's own row the ONE row in this list that could
    /// never report a failed toggle — in the feature whose whole point is that
    /// brisk holds itself to the standard it preaches.
    [Fact]
    public async Task StartupList_BriskToggle_ReportsAFailedToggleLikeAnyOtherRow()
    {
        var runner = new TaskStateRunner { TaskExists = true, DeleteSucceeds = false };
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"));
        await state.ScanAsync();

        var briskRow = vm.Items.Single(i => i.Hive == StartupItemRow.TaskHive);
        briskRow.IsEnabled = false;

        Assert.True(vm.ToggleFailed);
        Assert.True(briskRow.IsEnabled);   // reverted, like every other row
    }

    /// WAVE B, B3. Two owners that never reconciled: this checkbox read
    /// settings.json while the Startup page's brisk row read the real task, so
    /// turning brisk off there left the checkbox still showing "on".
    [Fact]
    public void Settings_StartWithWindows_ReadsTheTask_NotTheStoredFlag()
    {
        var settings = new Settings { StartWithWindows = true };   // stale record
        var runner = new TaskStateRunner { TaskExists = false };    // the truth
        var vm = new SettingsViewModel(settings, Path.Combine(_root, "s.json"),
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"),
            _ => { }, _ => { });

        Assert.False(vm.StartWithWindows);
    }

    /// Seen in a live window: with Language switched to English the Theme box
    /// still read "Koyu". The labels were resolved by a converter bound to
    /// LabelKey — the same string in every language — so the binding never
    /// re-evaluated and WPF kept the text it built once. The label is read
    /// live now, and each option announces that it reads differently.
    ///
    /// The list itself deliberately does NOT move; ChoiceComboBoxTests holds
    /// both halves of why, on a real ComboBox.
    [Fact]
    public void LanguageChange_RelabelsBothDropdowns()
    {
        var loc = new Loc();
        loc.SetLanguage("en");
        var vm = new SettingsViewModel(new Settings(),
            Path.Combine(_root, "lang.json"),
            new StartupLauncher(new TaskStateRunner(), new FakeRegistry(),
                @"C:\x\brisk-app.exe"),
            _ => { }, loc.SetLanguage, loc);
        var dark = vm.ThemeOptions.Single(o => o.Value == "dark");
        var announced = 0;
        dark.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChoiceOption.Label)) announced++;
        };
        Assert.Equal("Dark", dark.Label);

        vm.Language = "tr";

        Assert.Equal("Koyu", dark.Label);
        Assert.Equal(1, announced);
        Assert.Equal("Türkçe", vm.LanguageOptions.Single(o => o.Value == "tr").Label);
    }

    /// ...and a schtasks that refuses must not leave settings.json claiming an
    /// autostart that does not exist.
    [Fact]
    public void Settings_StartWithWindows_RefusedByWindows_IsReported()
    {
        var path = Path.Combine(_root, "refused.json");
        var settings = new Settings();
        var runner = new TaskStateRunner { CreateSucceeds = false };
        var vm = new SettingsViewModel(settings, path,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"),
            _ => { }, _ => { });

        vm.StartWithWindows = true;

        Assert.True(vm.StartupFailed);
        Assert.False(vm.StartWithWindows);        // the checkbox tells the truth
        Assert.False(settings.StartWithWindows);  // and nothing was persisted
    }

    [Fact]
    public async Task Startup_DescribesKnownApps_AndFlagsSystemEntries()
    {
        var loc = EnglishLoc();
        var host = new FakeEngineHost();
        host.Startup.Add(new StartupEntry("HKCU", "com.squirrel.Teams.Teams", true, true));
        host.Startup.Add(new StartupEntry("HKCU", "OneDrive", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "SecurityHealth", true, false));
        host.Startup.Add(new StartupEntry("HKLM", "RandomOemService", true, false));
        host.Startup.Add(new StartupEntry("HKCU", "MyTool", true, false));
        var state = new AppState(host);
        var vm = new StartupViewModel(state, host, loc, () => false, OffLauncher());
        await state.ScanAsync();

        string Desc(string name) => vm.Items.Single(i => i.Name == name).Description;
        // known apps match by contained name, OrdinalIgnoreCase
        Assert.Equal(loc["startup.app.teams"], Desc("com.squirrel.Teams.Teams"));
        Assert.Equal(loc["startup.app.onedrive"], Desc("OneDrive"));
        // system-ish: known system names, and HKLM outside the heavy table
        Assert.Equal(loc["startup.system.hint"], Desc("SecurityHealth"));
        Assert.Equal(loc["startup.system.hint"], Desc("RandomOemService"));
        // unknown user apps get no invented description
        Assert.Equal("", Desc("MyTool"));
    }

    [Fact]
    public async Task StartupList_IncludesBriskItself_WhenAutostartIsOn()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 0 };   // task exists
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"));

        await state.ScanAsync();

        Assert.Contains(vm.Items, i => i.Name == "brisk");
    }

    [Fact]
    public async Task StartupList_OmitsBrisk_WhenAutostartIsOff()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 1 };   // no task
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"));

        await state.ScanAsync();

        Assert.DoesNotContain(vm.Items, i => i.Name == "brisk");
    }

    // Pins the toggle's destination: brisk's row must reach StartupLauncher
    // (schtasks), never _host.SetStartupEnabled -- the engine's StartupManager
    // has no idea brisk's autostart is a Scheduled Task. A refactor that
    // collapsed this closure into the host-backed one would silently no-op
    // while still reporting success; this test fails loudly if that happens.
    [Fact]
    public async Task StartupList_BriskToggle_RoutesToLauncher_NotHost()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 0 };   // task exists
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => false,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"));

        await state.ScanAsync();

        var briskRow = vm.Items.Single(i => i.Name == "brisk");
        briskRow.IsEnabled = false;

        Assert.Contains(runner.Calls,
            c => c.Exe == "schtasks.exe" && c.Args.Contains("/Delete"));
        Assert.Empty(host.StartupToggles);
    }

    // Pins that a successful toggle clears a stale ToggleFailed banner --
    // otherwise a dry-run (or an earlier failed host-row toggle) leaves the
    // warning on screen even though brisk's own toggle just succeeded.
    [Fact]
    public async Task StartupList_BriskToggle_ClearsToggleFailed_OnSuccess()
    {
        var host = new FakeEngineHost();
        var state = new AppState(host);
        var runner = new FakeProcessRunner { NextExitCode = 0 };   // task exists
        var dryRun = true;
        var vm = new StartupViewModel(state, host, EnglishLoc(), () => dryRun,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"));

        await state.ScanAsync();
        var briskRow = vm.Items.Single(i => i.Name == "brisk");

        briskRow.IsEnabled = false;   // dry run: fails, sets ToggleFailed
        Assert.True(vm.ToggleFailed);

        dryRun = false;
        briskRow.IsEnabled = false;   // now succeeds: must clear ToggleFailed
        Assert.False(vm.ToggleFailed);
    }

    [Fact]
    public void Settings_SettersPersistAndApply()
    {
        var path = Path.Combine(_root, "settings.json");
        var settings = new Settings();
        // The checkbox reads the real task now (B3), so the fake has to
        // behave like schtasks: /Query answers 0 only after a /Create.
        var runner = new TaskStateRunner();
        var applied = new List<string>();
        var vm = new SettingsViewModel(settings, path,
            new StartupLauncher(runner, new FakeRegistry(), @"C:\x\brisk-app.exe"),
            theme => applied.Add("theme:" + theme),
            lang => applied.Add("lang:" + lang));

        // "light" — a real change from the dark default, so the setter's
        // no-change guard doesn't swallow the apply callback.
        vm.Theme = "light";
        vm.Language = "tr";
        vm.DryRun = true;
        vm.StartWithWindows = true;

        Assert.Equal(new[] { "theme:light", "lang:tr" }, applied);
        var reloaded = Settings.Load(path);
        Assert.Equal("light", reloaded.Theme);
        Assert.Equal("tr", reloaded.Language);
        Assert.True(reloaded.DryRun);
        Assert.True(reloaded.StartWithWindows);
        Assert.Contains(runner.Calls,
            c => c.Exe == "schtasks.exe" && c.Args.Contains("/Create"));

        vm.StartWithWindows = false;
        Assert.Contains(runner.Calls,
            c => c.Exe == "schtasks.exe" && c.Args.Contains("/Delete"));
    }

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
