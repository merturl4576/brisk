using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using BriskEngine.Diagnostics;
using BriskEngine.Diagnostics.Rules.Privacy;
using BriskEngine.Models;

namespace Brisk.ViewModels;

/// One line of the read-back: a switch brisk turned off, and what looking
/// again found. The verdict is the engine's (ReadBackState); everything here
/// is rendering — which sentence it takes, what argument that sentence needs,
/// and the arithmetic ReadBack deliberately does not do.
public sealed class ReadBackRow
{
    public ReadBackRow(ReadBackResult result, Loc loc, DateTime nowUtc,
        Func<ReadBackRow, Task> undo)
    {
        RuleId = result.RuleId;
        // The PAST-TENSE label, not the rule's finding title. A finding title
        // is a sentence about the switch being ON — "The advertising ID is
        // not switched off" — and over a line that reads "you switched this
        // off 3 days ago; it still reads as off" it contradicts the sentence
        // under it. DoneLabel is what the journal report and the report card
        // already put on a fix brisk applied, so all three surfaces name one
        // act one way.
        Title = DoneLabel.For(loc, result.RuleId,
            $"rule.{result.RuleId}.title", result.RuleId);
        State = result.State;
        Text = Sentence(result, loc, nowUtc);
        StateBrushKey = BrushKeyFor(result.State);
        UndoCommand = new RelayCommand(() => _ = undo(this));
    }

    public string RuleId { get; }
    public string Title { get; }
    public ReadBackState State { get; }
    public string Text { get; }
    /// The dot beside the line. Held is the only good news here; a write that
    /// was taken away and a write this edition is not acting on both want the
    /// reader's attention, and the state where brisk cannot tell gets the
    /// quiet colour rather than either of the two verdicts it did not reach.
    public string StateBrushKey { get; }
    public RelayCommand UndoCommand { get; }

    /// Which key, and which argument. The four sentences do NOT take the same
    /// one — `readback.reverted` takes a date, `readback.held` and
    /// `readback.unverified` take a day count, and `readback.ignored` takes
    /// nothing at all — so passing the wrong one renders "You switched this
    /// off 2026-08-12 days ago" without failing anything. That is what
    /// EachRow_RendersItsOwnSentenceWithItsOwnArgument pins.
    ///
    /// The final arm throws rather than picking a sentence, the same way
    /// ReadBack.StateOf refuses to pick a state: a fifth member added to
    /// ReadBackState without a line here would otherwise be rendered with
    /// copy written for a different verdict.
    private static string Sentence(ReadBackResult result, Loc loc, DateTime nowUtc) =>
        result.State switch
        {
            ReadBackState.Held =>
                loc.F("readback.held", DaysAgo(result.FixedAtUtc, nowUtc)),
            ReadBackState.Reverted =>
                loc.F("readback.reverted", LocalDate(result.FixedAtUtc)),
            ReadBackState.WrittenButIgnored => loc["readback.ignored"],
            ReadBackState.WrittenButUnverified =>
                loc.F("readback.unverified", DaysAgo(result.FixedAtUtc, nowUtc)),
            var unknown => throw new ArgumentOutOfRangeException(
                nameof(result), unknown,
                $"'{result.RuleId}' came back in a read-back state this row has " +
                "no sentence for"),
        };

    private static string BrushKeyFor(ReadBackState state) => state switch
    {
        ReadBackState.Held => "Good",
        ReadBackState.Reverted => "SeverityWarning",
        ReadBackState.WrittenButIgnored => "SeverityWarning",
        ReadBackState.WrittenButUnverified => "TextFaint",
        var unknown => throw new ArgumentOutOfRangeException(
            nameof(state), unknown, "no colour for this read-back state"),
    };

    /// "{0} gün önce" — counted in LOCAL CALENDAR DAYS, not in 24-hour ticks.
    /// FixedAtUtc is UTC and the sentence a user reads is local, and the two
    /// part company at the ends of a day: a fix applied at 23:00 local on
    /// Monday is "yesterday" to the person reading it at 08:00 on Tuesday,
    /// and a tick count would answer 0. So both sides are converted to local
    /// time and reduced to their DATE before subtracting.
    ///
    /// Never negative. A stamp in the future is a machine whose clock moved —
    /// ReadBack carries one through untouched on purpose — and "-3 days ago"
    /// would be nonsense on screen, so it floors at today. That is a
    /// rendering choice and not a correction: brisk has no idea which of the
    /// two readings is the wrong one.
    internal static int DaysAgo(DateTime fixedAtUtc, DateTime nowUtc)
    {
        var days = (nowUtc.ToLocalTime().Date - fixedAtUtc.ToLocalTime().Date).Days;
        return days < 0 ? 0 : days;
    }

    /// The same date format the report card prints a journal entry with
    /// (ReportCardModel.FixRows), so one fix cannot wear two spellings of its
    /// own date across two brisk surfaces.
    internal static string LocalDate(DateTime utc) => utc.ToLocalTime()
        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// The Privacy page: what this machine has written down, what its switches
/// currently read as, and what happened to the ones brisk turned off.
///
/// Built on HealthViewModel's shape rather than beside it — it subscribes to
/// AppState.Changed, rebuilds in Refresh, and its finding rows are the same
/// FindingRow with the same fix lifecycle. What differs is the GROUPING, and
/// the grouping is the product decision this page exists to carry: the four
/// switches with no visible consequence share ONE button, and the two that
/// cost the user something get a switch each with the loss named beside them.
///
/// There is no score on this page and there is no gauge, deliberately.
/// Privacy is a second axis: the health score grades speed and hygiene, every
/// finding here is a Notice, and none of them moves it. A ring reading 100
/// over a page of switches that are on would be a claim nobody measured — the
/// same defect as the impact meter this page suppresses.
public sealed class PrivacyViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly Loc _loc;
    private readonly Func<bool> _isDryRun;
    private readonly Func<string, bool> _openWindowsSetting;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<Task> _morphPause;
    private string _message = "";
    private string _turnOffSafeText = "";
    private bool _busy;

    /// `openWindowsSetting` has no default, deliberately. It is what opens
    /// Windows' own Settings app for the one row brisk points somewhere else
    /// for, and a row whose opener is missing withholds its link silently —
    /// so the compiler is made to ask every construction site for one rather
    /// than a source-reading test being written to notice afterwards. It
    /// returns whether the Settings app actually started: a click that
    /// reached nothing is reported here, not swallowed.
    public PrivacyViewModel(AppState state, IEngineHost host, Loc loc,
        Func<bool> isDryRun, Func<string, bool> openWindowsSetting,
        Func<DateTime>? utcNow = null, Func<Task>? morphPause = null)
    {
        _state = state;
        _host = host;
        _loc = loc;
        _isDryRun = isDryRun;
        _openWindowsSetting = openWindowsSetting;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _morphPause = morphPause ?? (() => Task.Delay(HealthViewModel.FixedMorphMs));
        _state.Changed += Refresh;
        ScanCommand = new RelayCommand(() => _ = _state.ScanAsync());
        // Enabled only while there is a consequence-free switch left to turn
        // off, read off the same predicate the button's own walk uses — the
        // caption counts what the click will do, so the count and the action
        // cannot promise different things.
        TurnOffSafeCommand = new RelayCommand(() => _ = TurnOffSafeAsync(),
            () => SafeSwitchRows.Count > 0);
    }

    /// The four consequence-free switches, and the ONE button that turns them
    /// off. Public and static because the caption counts them and the walk
    /// acts on them, and those have to be the same question — the same reason
    /// FixAllService.IsOneClickFixable is public.
    ///
    /// Auto is the consent level for "no visible consequence"; the two that
    /// cost the user something override it to Confirm precisely so a button
    /// carrying one consent cannot reach the other. That is why this asks the
    /// category rather than naming four rule ids: a fifth consequence-free
    /// switch joins the button by shipping as Auto, and a rule that starts
    /// costing something leaves it by shipping as Confirm.
    public static bool IsConsequenceFree(DiagnosticFinding finding) =>
        FindingSections.IsPrivacy(finding) && finding.CanFix
        && finding.Category == RuleCategory.Auto;

    /// The other tier: a privacy switch brisk can flip whose consent level
    /// says the user has to be told what it costs first.
    public static bool CostsTheUserSomething(DiagnosticFinding finding) =>
        FindingSections.IsPrivacy(finding) && finding.CanFix
        && finding.Category == RuleCategory.Confirm;

    /// A privacy finding that reports no reading at all. The absence of a
    /// Headline is what says so — the disclosure family's own contract, since
    /// a headline is what a finding leads with and leading with a reading that
    /// never arrived is the same lie in a larger font — and the two tiers
    /// above are asked first, because the six switches carry no headline
    /// either and none of them attempted a reading to fail at.
    ///
    /// Public and static for the same reason IsConsequenceFree is: the report
    /// card's own "what brisk could not read" section is fed from here too, so
    /// the card and this page answer the question once instead of keeping two
    /// lists that can drift.
    public static bool IsUnreadableDisclosure(DiagnosticFinding finding) =>
        FindingSections.IsPrivacy(finding)
        && !IsConsequenceFree(finding) && !CostsTheUserSomething(finding)
        && finding.Headline is null;

    /// The numbers, largest first — and only over the readings that ARE
    /// numbers. See TheDisclosureRows_LeadWithTheLargestNumber for why this
    /// stops there: 47 devices, 1284 records and 1.2 GB uploaded are not the
    /// same kind of quantity, and a comparator that ranked them against each
    /// other would be inventing an order nobody measured.
    public ObservableCollection<FindingRow> DisclosureRows { get; } = new();

    /// The disclosures that read nothing. They carry no Headline — the
    /// disclosure family's own contract, because "a headline is what a
    /// finding leads with, and leading with a reading that never arrived is
    /// the same lie in a larger font" — and that absence is what puts them
    /// here rather than a second list of ids maintained beside the rules.
    /// The spec's fourth red line is why they get a place of their own at
    /// all: what could not be read is never a silent zero.
    public ObservableCollection<FindingRow> UnreadableRows { get; } = new();

    /// WHAT THAT RECORD HOLDS: one line per USB storage instance Windows
    /// wrote down, model and both dates, behind a fold on this page and
    /// nowhere else in brisk.
    ///
    /// The spec's red line 2 said "device names never", and on 2026-08-26 the
    /// maintainer amended it at his first live look: the model and its dates
    /// are the user's own data, and the page only the user looks at may show
    /// the record in full. Every surface built to be shared still carries the
    /// count alone — and cannot carry more, because the names arrive here on
    /// ScanSnapshot.UsbDevices and never on a DiagnosticFinding. The card is
    /// not trusted with them; it is never handed them.
    ///
    /// Strings and not records, deliberately. The formatting is a rendering
    /// decision — which sentence, which date spelling, what a date brisk did
    /// not read looks like — and it belongs on the side of the split that
    /// knows the language. What the markup binds is a list of lines.
    public ObservableCollection<string> UsbDeviceRows { get; } = new();

    /// The four the one button turns off.
    public ObservableCollection<FindingRow> SafeSwitchRows { get; } = new();

    /// The two that cost something, each on its own control, each with
    /// FindingRow.CostText beside it.
    public ObservableCollection<FindingRow> CostlySwitchRows { get; } = new();

    /// What brisk found when it looked again at the switches it turned off,
    /// newest fix first. Straight from the snapshot: the read-back was taken
    /// in the same pass that produced the findings above, which is what lets
    /// a reverted switch and a switch brisk is reporting again be the same
    /// live read rather than two.
    public ObservableCollection<ReadBackRow> ReadBackRows { get; } = new();

    public AppState State => _state;
    /// Whether either tier has anything on it. The page says "every switch
    /// brisk reads here already reads as off" off this, rather than leaving
    /// a gap between two headings that a reader has to interpret.
    public bool HasSwitches => SafeSwitchRows.Count > 0 || CostlySwitchRows.Count > 0;
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }

    /// The one button's caption, carrying the count it will act on.
    public string TurnOffSafeText
    {
        get => _turnOffSafeText;
        private set => Set(ref _turnOffSafeText, value);
    }

    public RelayCommand ScanCommand { get; }
    public RelayCommand TurnOffSafeCommand { get; }

    /// The revelation band's "see the evidence", for a privacy finding.
    /// Every band on this page is searched: which of the four a rule landed
    /// in is this page's business and not the caller's.
    public void ExpandFinding(string ruleId)
    {
        foreach (var row in DisclosureRows.Concat(UnreadableRows)
                     .Concat(SafeSwitchRows).Concat(CostlySwitchRows))
            if (string.Equals(row.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            {
                row.IsExpanded = true;
                return;
            }
    }

    /// The one button. It walks THIS page's safe rows, which are the
    /// snapshot's consequence-free privacy findings and nothing else, so a
    /// Confirm switch cannot be reached from here however the page is driven.
    ///
    /// A refusal is reported rather than swallowed. diagnostic-level writes
    /// under HKLM, so on an unelevated machine it fails cleanly through
    /// FixRunner with Ok:false while the other three succeed — the ordinary
    /// outcome of this button on a standard account, not an exotic one. The
    /// batch keeps going, and the sentence afterwards says how many refused
    /// and hands over exactly what the attempt reported.
    ///
    /// VERBATIM is the point and also the cost. FixOutcome.Message is the
    /// engine's own words, and a .NET registry exception carries the full key
    /// path in its text — so a refusal can put HKLM\SOFTWARE\Policies\... on
    /// screen. That is a path on the user's own machine rather than anything
    /// this wave's red lines are about (a device name, a program name, a
    /// count's contents), and paraphrasing it would leave the one person who
    /// can act on the refusal without the thing they need. The same choice
    /// HealthViewModel makes with the same field.
    public async Task TurnOffSafeAsync()
    {
        if (_busy || _state.IsAwaitingDisplayConfirmation) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            var rows = SafeSwitchRows.ToList();
            if (rows.Count == 0) return;
            var refused = new List<string>();
            foreach (var row in rows)
            {
                row.BeginFix();          // instant feedback, before any await
                var outcome = await Task.Run(() => _host.Fix(row.RuleId));
                row.CompleteFix(outcome.Ok);
                if (!outcome.Ok) refused.Add(outcome.Message);
            }
            Message = refused.Count == 0 ? ""
                : _loc.F("privacy.turnoff.refused", refused.Count,
                    string.Join(" · ", refused));
            if (refused.Count < rows.Count) await _morphPause();
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// One switch, from its own card — the route the two costly ones take,
    /// and the route a safe one still offers for somebody who wants to pick.
    public async Task FixAsync(FindingRow row)
    {
        if (_busy || _state.IsAwaitingDisplayConfirmation) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            row.BeginFix();              // instant feedback, before any await
            var outcome = await Task.Run(() => _host.Fix(row.RuleId));
            row.CompleteFix(outcome.Ok);
            Message = outcome.Ok ? "" : outcome.Message;
            if (outcome.Ok) await _morphPause();
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// The one control on this page that hands the reader somewhere else
    /// instead of changing something here: it opens Windows' own Settings app
    /// at the row's page and stops there.
    ///
    /// Recall is report-only on purpose — the surface is new, differs between
    /// builds, and a fix brisk cannot check afterwards is the one thing this
    /// project refuses to ship — so what the page offers is the place the
    /// user can change it themselves. Nothing is journalled, because nothing
    /// was done; a refusal is said out loud, because the alternative is a
    /// button that swallows its click.
    private void OpenWindowsSetting(FindingRow row) =>
        Message = _openWindowsSetting(row.WindowsSettingUri)
            ? "" : _loc["privacy.setting.failed"];

    /// Undo, from a read-back line's context menu — the same quiet gesture
    /// the journal report rows carry, and the page's answer to "all of it
    /// reversible". A read-back line exists exactly where brisk has a
    /// journalled fix to put back, so that is where the affordance sits.
    public Task UndoAsync(ReadBackRow row) => UndoRuleAsync(row.RuleId);

    /// The same undo reached from a finding card. One body behind both, so
    /// the two routes cannot start meaning different things.
    public Task UndoAsync(FindingRow row) => UndoRuleAsync(row.RuleId);

    private async Task UndoRuleAsync(string ruleId)
    {
        if (_busy) return;
        IsBusy = true;                   // set before the first await — re-entry guard
        try
        {
            if (_isDryRun())
            {
                Message = _loc["dryrun.blocked"];
                return;
            }
            var outcome = await Task.Run(() => _host.Undo(ruleId));
            Message = outcome.Ok ? "" : outcome.Message;
            await _state.ScanAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Refresh()
    {
        var snapshot = _state.Snapshot;
        if (snapshot is null) return;
        var undoable = _host.ListUndoable().Select(u => u.RuleId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        DisclosureRows.Clear();
        UnreadableRows.Clear();
        SafeSwitchRows.Clear();
        CostlySwitchRows.Clear();
        // Ordered before it is split, so each band keeps the one order this
        // page sorts by. The two switch bands are small and fixed; the
        // disclosure band is where the order is the product decision, and
        // LeadingNumber is what carries it.
        foreach (var finding in snapshot.Findings
                     .Where(FindingSections.IsPrivacy)
                     .OrderByDescending(LeadingNumber)
                     .ThenBy(f => f.RuleId, StringComparer.Ordinal))
            Band(finding).Add(new FindingRow(finding, _loc,
                undoable.Contains(finding.RuleId),
                row => _ = FixAsync(row), row => _ = UndoAsync(row),
                onOpenWindowsSetting: OpenWindowsSetting));

        UsbDeviceRows.Clear();
        foreach (var device in snapshot.UsbDevices)
            UsbDeviceRows.Add(_loc.F("privacy.usb.device", device.Model,
                Stamp(device.FirstSeen), Stamp(device.LastSeen)));

        ReadBackRows.Clear();
        var now = _utcNow();
        foreach (var result in snapshot.ReadBack.OrderByDescending(r => r.FixedAtUtc))
            ReadBackRows.Add(new ReadBackRow(result, _loc, now, UndoAsync));

        TurnOffSafeText = _loc.F("privacy.turnoff.safe", SafeSwitchRows.Count);
        Raise(nameof(HasSwitches));
        TurnOffSafeCommand.RaiseCanExecuteChanged();
    }

    /// Which band a privacy finding belongs in. The two switch tiers are
    /// tested for and the two report-only bands are what is LEFT — so a
    /// privacy finding this page has no tier for still lands somewhere and is
    /// still shown, rather than being dropped between four predicates.
    ///
    /// The fall-through is not one band but two, and they are not equally
    /// safe to fall into. A finding with a reading lands under the numbers,
    /// whose heading claims that Windows wrote something down and NOTHING
    /// about what this page will do with it. recall-status is the row that
    /// settles which of those two things the band means: it carries a
    /// Headline, so it lands here, and it carries the link to Windows' own
    /// setting, so this band holds a row with a control on it. What the band
    /// promises about is the READING; what it does not hold is a switch brisk
    /// will flip, and the two tiers above are where those go.
    ///
    /// A finding with NO Headline lands under "what brisk could not read",
    /// which IS a claim about the read, and the only thing tested for it is
    /// the absence of a headline. That is the disclosure family's own
    /// contract and holds for every rule this wave ships; a future privacy
    /// rule that is Confirm, not fixable and headless for some other reason
    /// would be described as unreadable by a page that never asked. Flagged rather than guarded: the alternative is a list of
    /// "the unreadable ones" maintained beside the rules, which is the second
    /// channel this page was built to avoid.
    ///
    /// That last band is asked through IsUnreadableDisclosure rather than
    /// inline, because the report card's own unread section asks the same
    /// question and the two surfaces must not come to answer it differently.
    private ObservableCollection<FindingRow> Band(DiagnosticFinding finding) =>
        IsConsequenceFree(finding) ? SafeSwitchRows
        : CostsTheUserSomething(finding) ? CostlySwitchRows
        : IsUnreadableDisclosure(finding) ? UnreadableRows
        : DisclosureRows;

    /// A date brisk read, or the dash this app prints where it has no
    /// reading. Never a guess and never a blank, which an eye reads as a
    /// zero: FirstSeen and LastSeen are null when the read was REFUSED or
    /// unreadable, and "brisk did not read one" is what the dash says.
    ///
    /// NOT converted to local time, and that is the decision rather than an
    /// oversight. The usb row directly above this fold prints "the oldest
    /// date it could read among them is 2017-05-09" straight off the rule,
    /// unconverted — so converting here would put two spellings of one
    /// record's own date six lines apart on one page. ReadBackRow.LocalDate
    /// converts because what it dates is an act the user performed at a wall
    /// clock; what this dates is a stamp Windows wrote, and the rule that
    /// reads it is the surface this has to agree with.
    ///
    /// yyyy-MM-dd invariant, the same shape the rule's evidence uses.
    private static string Stamp(DateTime? utc) => utc is { } when
        ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        : "—";

    /// The number a disclosure leads with, for "largest first", and
    /// long.MinValue for every reading that is not one.
    ///
    /// Headline.Value is the engine's own formatted English, and the counts
    /// are written into it with the invariant culture, so "1284" parses here
    /// whatever language the GUI is in. What deliberately does NOT parse is
    /// "1.2 GB" and "Off": a byte amount and a policy word are not quantities
    /// this page can rank against a device count, and inventing an order for
    /// them would be exactly the unearned claim the impact meter was
    /// suppressed for. They sort after every number, by rule id, which is an
    /// order that says nothing rather than one that says something false.
    private static long LeadingNumber(DiagnosticFinding finding) =>
        finding.Headline is { } headline
        && long.TryParse(headline.Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var value)
            ? value : long.MinValue;
}
