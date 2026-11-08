using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Actions;
using BriskEngine.Actions.Rules;
using BriskEngine.Diagnostics;
using Brisk.Localization;

namespace Brisk.Session;

/// What brisk does to a row: stops a startup app, turns a setting on or
/// off, deletes files.
public enum RowAct { Stop, TurnOn, TurnOff, Delete }

/// One place for a row's words (ruling R34): a startup app is stopped, a
/// setting is turned on or off by what brisk does to it, and the Speed
/// button counts apps and settings apart. The pages and the Home strip use
/// the same words.
public static class RowWords
{
    /// The Speed settings brisk switches on; every other setting it turns off.
    private static readonly HashSet<string> TurnedOn = new(StringComparer.OrdinalIgnoreCase)
    {
        "speed.power-mode", "speed.gpu-scheduling", "speed.windowed-games",
    };

    public static bool IsStartupApp(ActionItem item) => StartupEntryRule.TryParseId(item.Id, out _, out _);

    public static RowAct ActOf(ActionItem item) =>
        item.Group == ActionGroupKind.Space ? RowAct.Delete
        : IsStartupApp(item) ? RowAct.Stop
        : TurnedOn.Contains(item.Id) ? RowAct.TurnOn
        : RowAct.TurnOff;

    /// Startup apps and settings among `items`; Space rows are neither.
    public static (int Apps, int Settings) Count(IEnumerable<ActionItem> items)
    {
        int apps = 0, settings = 0;
        foreach (var item in items)
        {
            if (item.Group == ActionGroupKind.Space) continue;
            if (IsStartupApp(item)) apps++; else settings++;
        }
        return (apps, settings);
    }

    public static string Verb(ActionItem item, Loc loc) => loc[$"gui.row.verb.{Word(ActOf(item))}"];

    public static string DoneWord(ActionItem item, Loc loc) => loc[$"gui.row.done.{Word(ActOf(item))}"];

    private static string Word(RowAct act) => act switch
    {
        RowAct.Stop => "stop",
        RowAct.TurnOn => "on",
        RowAct.TurnOff => "off",
        _ => "delete",
    };

    /// Hızlandır's and Gizlilik's one button: "2 uygulamayı durdur", "2 ayarı
    /// değiştir", "3 değişikliği yap", "2 ayarı kapat"; nothing selected says so.
    public static string Button(ActionGroupKind group, IReadOnlyCollection<ActionItem> selected, Loc loc)
    {
        if (selected.Count == 0) return loc["gui.page.button.none"];
        if (group == ActionGroupKind.Privacy) return Counted("gui.page.button.privacy", selected.Count, loc);
        var (apps, settings) = Count(selected);
        var key = settings == 0 ? "gui.page.button.apps"
            : apps == 0 ? "gui.page.button.settings"
            : "gui.page.button.mixed";
        return Counted(key, apps + settings, loc);
    }

    public static string Apps(int n, Loc loc) => Counted("gui.home.strip.apps", n, loc);

    public static string Settings(int n, Loc loc) => Counted("gui.home.strip.settings", n, loc);

    /// English says "1 app" and "2 apps": a count of one reads the ".one" key.
    public static string Counted(string key, int n, Loc loc) => loc.F(n == 1 ? key + ".one" : key, n);

    /// What went through: done or waiting for a restart, and not put back
    /// (or ignored) by Windows.
    public static bool Succeeded(RowResult r) =>
        r.Outcome is RowOutcome.Done or RowOutcome.PendingRestart
        && r.ReadBack is not (ReadBackState.Reverted or ReadBackState.WrittenButIgnored);
}
