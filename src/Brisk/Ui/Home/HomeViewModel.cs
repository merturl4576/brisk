using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BriskEngine.Actions;
using BriskEngine.Diagnostics;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Home;

/// The parts the floor satellites stand for, in floor order: front, right, left.
public enum Part { Cpu, Gpu, Ram }

/// The right column's spec blocks, one per icon.
public enum SpecBlock { Disk, Cpu, Gpu, Ram }

/// One row of a column: a name and its value.
public sealed record InfoLine(string Name, string Value);

/// The hero gauge for the selected part. Percent and Value: the live load
/// (null and "—" when unread). Temperature: "66 °C", "—" when the part's
/// sensor did not answer, null for memory (it has none: the line hides).
/// Hot: the heat bar's warm colours. Caption: the part's own name, or the
/// memory in use; null hides it.
public sealed record GaugeFigures(string Label, double? Percent, string Value, double Fraction,
    string? Temperature, bool Hot, string? Caption);

/// The Pil block. Health and Cycles are null when the battery does not say:
/// those lines hide.
public sealed record BatteryFigures(string Charge, string State, bool PluggedIn, string? Health, string? Cycles);

/// The live left column and the satellites' tags; "—" wherever the sample
/// had nothing. Battery null = no battery: the Pil block hides.
public sealed record LiveFigures(string Upload, string Download, BatteryFigures? Battery,
    string CpuTemp, string GpuTemp, string CpuLoad, string GpuLoad, string RamLoad);

/// The right column's spec blocks; "—" for one that could not be read.
public sealed record SpecFigures(string Cpu, string Gpu, string Ram, string Disk);

/// The bottom strip: a muted lead ("brisk hazır:", "Son işlem:"), the
/// figures, the button (null hides it), and Geri al (only while the last run
/// can be undone: its Speed and Privacy settings, never deleted files).
public sealed record HomeStrip(string Lead, string Body, Tone Tone, string? Button, bool ButtonEnabled,
    HomeButton ButtonAction, bool ShowUndo)
{
    public string Text => Lead.Length == 0 ? Body : $"{Lead} {Body}";
}

/// Genel: the live system monitor. The gauge shows the selected part's load
/// and temperature; the floor satellites pick the part (and only they turn
/// the gauge); the right column's icons only light a spec block. The
/// monitor is sampled once a second off the UI thread while Home is on
/// screen, never two samples at once; the specs are read once, also off the
/// UI thread. Until the first sample lands every reading is "—". The strip
/// is Hepsini yap: what it takes at rest, what it did after, and Geri al.
public sealed class HomeViewModel : ViewModelBase
{
    public static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan ReceiptEvery = TimeSpan.FromSeconds(30);
    public const int TopRows = 3;
    /// The heat bar warms from here (the mockup's threshold).
    public const double HotFromC = 58;

    private const string Dash = "—";
    private const string Sep = " · ";
    /// Where each part stands on the floor: front, right, left.
    private static readonly double[] FloorBase = { 0, 120, 240 };

    private readonly Action<Action> _post;
    private readonly Action<Action> _background;
    private readonly ActionSession _session;
    private readonly IActionBackend _backend;
    private readonly IUiTimers _timers;
    private readonly Loc _loc;
    private readonly Action<ActionGroupKind> _open;
    private readonly List<IDisposable> _running = new();
    private MonitorSample? _sample;
    private SpecsInfo? _specsRead;
    private bool _sampling, _specsAsked;

    public HomeViewModel(Action<Action> post, Action<Action> background, ActionSession session,
        IActionBackend backend, IUiTimers timers, Loc loc, Action<ActionGroupKind> open)
    {
        _post = post;
        _background = background;
        _session = session;
        _backend = backend;
        _timers = timers;
        _loc = loc;
        _open = open;
        Primary = new RelayCommand(OnPrimary, () => Strip.ButtonEnabled);
        Undo = new RelayCommand(() => _ = _session.UndoAsync(), () => _session.CanUndo);
        OpenFailed = new RelayCommand(OnOpenFailed);
        SelectCpu = new RelayCommand(() => SelectPart(Part.Cpu));
        SelectGpu = new RelayCommand(() => SelectPart(Part.Gpu));
        SelectRam = new RelayCommand(() => SelectPart(Part.Ram));
        ShowDiskSpec = new RelayCommand(() => HighlightSpec(SpecBlock.Disk));
        ShowCpuSpec = new RelayCommand(() => HighlightSpec(SpecBlock.Cpu));
        ShowGpuSpec = new RelayCommand(() => HighlightSpec(SpecBlock.Gpu));
        ShowRamSpec = new RelayCommand(() => HighlightSpec(SpecBlock.Ram));
        _strip = ComputeStrip();
        _gauge = ComputeGauge();
        _live = ComputeLive();
        _specs = ComputeSpecs();
        _top = ComputeTop();
        session.Changed += OnSessionChanged;
        loc.PropertyChanged += OnLanguageChanged;
    }

    public CultureInfo Culture => _loc.Culture;

    /// The view's fixed words: {Binding L[key]} follows a language switch.
    public Loc L => _loc;

    public RelayCommand Primary { get; }
    public RelayCommand Undo { get; }
    public RelayCommand OpenFailed { get; }
    public RelayCommand SelectCpu { get; }
    public RelayCommand SelectGpu { get; }
    public RelayCommand SelectRam { get; }
    public RelayCommand ShowDiskSpec { get; }
    public RelayCommand ShowCpuSpec { get; }
    public RelayCommand ShowGpuSpec { get; }
    public RelayCommand ShowRamSpec { get; }

    private Part _selected = Part.Cpu;
    public Part Selected { get => _selected; private set => Set(ref _selected, value); }

    private SpecBlock _highlighted = SpecBlock.Cpu;
    public SpecBlock HighlightedSpec { get => _highlighted; private set => Set(ref _highlighted, value); }

    /// Where the floor turns to: the selected part's spot comes to the front,
    /// the short way round. Not wrapped, so the view never spins the long way.
    private double _floorAngle;
    public double FloorAngle { get => _floorAngle; private set => Set(ref _floorAngle, value); }

    /// The gauge's rings: 120° a pick, against the floor's direction.
    private double _ringAngle;
    public double RingAngle { get => _ringAngle; private set => Set(ref _ringAngle, value); }

    /// While a pick's turn plays (TurnFor), the number counts and the arc
    /// sweeps to the new part's reading; a live reading every second lands
    /// at once, as in the mockup. Raised before Gauge changes.
    private bool _gliding;
    public bool Gliding { get => _gliding; private set => Set(ref _gliding, value); }

    public static readonly TimeSpan TurnFor = TimeSpan.FromSeconds(1);
    private DateTime _turnedAt = DateTime.MinValue;

    private GaugeFigures _gauge;
    public GaugeFigures Gauge { get => _gauge; private set => Set(ref _gauge, value); }

    private LiveFigures _live;
    public LiveFigures Live { get => _live; private set => Set(ref _live, value); }

    private SpecFigures _specs;
    public SpecFigures Specs { get => _specs; private set => Set(ref _specs, value); }

    /// "Şu an en çok kullanan" for the selected part.
    private IReadOnlyList<InfoLine> _top;
    public IReadOnlyList<InfoLine> Top { get => _top; private set => Set(ref _top, value); }

    private HomeStrip _strip;
    public HomeStrip Strip { get => _strip; private set => Set(ref _strip, value); }

    public bool IsActive => _running.Count > 0;

    public void Activate()
    {
        if (IsActive) return;
        _running.Add(_timers.Every(SampleEvery, Sample));
        _running.Add(_timers.Every(ReceiptEvery, PollReceipt));
        ReadSpecs();
        Sample();
    }

    public void Deactivate()
    {
        foreach (var t in _running) t.Dispose();
        _running.Clear();
    }

    // ---- the gauge, the floor and the right column

    /// A floor satellite: its part comes to the front and the gauge, its spec
    /// block and the top three follow. The part already in front turns nothing.
    public void SelectPart(Part part)
    {
        HighlightedSpec = part switch { Part.Gpu => SpecBlock.Gpu, Part.Ram => SpecBlock.Ram, _ => SpecBlock.Cpu };
        if (part == Selected) return;
        var delta = ShortWay(-FloorBase[(int)part] - FloorAngle);
        _turnedAt = _timers.UtcNow;
        Gliding = true;
        FloorAngle += delta;
        RingAngle += delta > 0 ? -120 : 120;
        Selected = part;
        Gauge = ComputeGauge();
        Top = ComputeTop();
    }

    /// A right-column icon: lights its spec block, and nothing else moves.
    public void HighlightSpec(SpecBlock block) => HighlightedSpec = block;

    /// In [-180, 180): the short way round to a spot.
    private static double ShortWay(double degrees) => ((degrees % 360) + 540) % 360 - 180;

    // ---- sampling

    private void Sample()
    {
        // One sample at a time: a tick while one is still out is skipped.
        if (_sampling) return;
        _sampling = true;
        _background(() =>
        {
            MonitorSample? sample = null;
            try { sample = _backend.SampleMonitor(); }
            catch (Exception) { }
            _post(() =>
            {
                _sampling = false;
                // A sample that failed is no reading: dashes, not the last numbers.
                _sample = sample;
                Gliding = _timers.UtcNow - _turnedAt < TurnFor;
                Live = ComputeLive();
                Gauge = ComputeGauge();
                Top = ComputeTop();
            });
        });
    }

    private void ReadSpecs()
    {
        if (_specsAsked) return;
        _specsAsked = true;
        _background(() =>
        {
            SpecsInfo? specs = null;
            try { specs = _backend.Specs(); }
            catch (Exception) { }
            _post(() =>
            {
                _specsRead = specs;
                Specs = ComputeSpecs();
                Gauge = ComputeGauge();
            });
        });
    }

    /// Until the receipt has its after-restart numbers (boot, RAM, telemetry).
    private void PollReceipt()
    {
        if (_session.LastRun is null || _session.Busy) return;
        if (_session.Receipt is { BootMsAfter: not null, StartupRamConfirmed: true, TelemetryAfter: not null }) return;
        _ = _session.RefreshReceiptAsync();
    }

    private void OnSessionChanged()
    {
        Strip = ComputeStrip();
        Primary.RaiseCanExecuteChanged();
        Undo.RaiseCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, PropertyChangedEventArgs e)
    {
        Raise(nameof(Culture));
        Strip = ComputeStrip();
        Gauge = ComputeGauge();
        Live = ComputeLive();
        Specs = ComputeSpecs();
        Top = ComputeTop();
    }

    // ---- figures

    private CultureInfo C => _loc.Culture;

    private string Whole(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", C);

    private string Percent(double? v) => v is { } p ? _loc.F("gui.home.percent", Whole(Math.Clamp(p, 0, 100))) : Dash;

    private string Celsius(double? v) => v is { } t ? $"{Whole(t)} °C" : Dash;

    private GaugeFigures ComputeGauge()
    {
        var s = _sample;
        var (label, load, temp, caption) = Selected switch
        {
            Part.Gpu => (_loc["gui.home.part.gpu"], s?.GpuPercent, (double?)s?.GpuTempC, GpuNames(" · ")),
            Part.Ram => (_loc["gui.home.part.ram"], s?.RamPercent, null, RamInUse(s)),
            _ => (_loc["gui.home.part.cpu"], s?.CpuPercent, s?.CpuTempC, Tidy(_specsRead?.CpuName)),
        };
        var percent = load is { } l ? Math.Clamp(l, 0, 100) : (double?)null;
        return new GaugeFigures(label, percent, percent is { } p ? Whole(p) : Dash, percent is { } f ? f / 100 : 0,
            Selected == Part.Ram ? null : Celsius(temp), temp >= HotFromC, caption);
    }

    /// "18,4 GB / 31,8 GB kullanılıyor"; RamUsedBytes is no reading while
    /// RamPercent is null.
    private string? RamInUse(MonitorSample? s)
    {
        if (s?.RamPercent is null) return null;
        var used = Gb(s.RamUsedBytes);
        return _specsRead?.RamUsableBytes is { } total
            ? _loc.F("gui.home.ram.used-of", used, Gb(total))
            : _loc.F("gui.home.ram.used", used);
    }

    private string? GpuNames(string separator) => _specsRead is { Gpus.Count: > 0 } specs
        ? string.Join(separator, specs.Gpus.Select(g => Tidy(g) ?? g))
        : null;

    private LiveFigures ComputeLive()
    {
        var s = _sample;
        BatteryFigures? battery = s?.Battery is { } b
            ? new BatteryFigures(Percent(b.ChargePercent),
                _loc[b.PluggedIn ? "gui.home.battery.plugged" : "gui.home.battery.unplugged"], b.PluggedIn,
                b.HealthPercent is { } h ? _loc.F("gui.home.percent", h.ToString(C)) : null,
                // A cycle count of 0 is a firmware that does not count (ruling R16).
                b.CycleCount is { } n and > 0 ? n.ToString(C) : null)
            : null;
        return new LiveFigures(Rate(s?.UpBytesPerSec), Rate(s?.DownBytesPerSec), battery,
            Celsius(s?.CpuTempC), Celsius(s?.GpuTempC),
            Percent(s?.CpuPercent), Percent(s?.GpuPercent), Percent(s?.RamPercent));
    }

    /// Network speed in bits a second, as the mockup and Task Manager show it.
    private string Rate(long? bytesPerSecond)
    {
        if (bytesPerSecond is not { } b || b < 0) return Dash;
        var bits = b * 8.0;
        if (bits < 1e6) return $"{Whole(bits / 1e3)} Kbps";
        if (bits < 1e9)
        {
            var m = Math.Round(bits / 1e6, 1, MidpointRounding.AwayFromZero);
            return $"{(m < 10 ? m.ToString("0.0", C) : Whole(m))} Mbps";
        }
        return $"{Math.Round(bits / 1e9, 1, MidpointRounding.AwayFromZero).ToString("0.0", C)} Gbps";
    }

    private IReadOnlyList<InfoLine> ComputeTop()
    {
        var list = Selected switch { Part.Gpu => _sample?.TopGpu, Part.Ram => _sample?.TopRam, _ => _sample?.TopCpu };
        // Empty is "not known" (or nothing using it): a dash, never zeros.
        if (list is not { Count: > 0 }) return new[] { new InfoLine(Dash, "") };
        return list.Take(TopRows).Select(p => new InfoLine(p.Name, Selected == Part.Ram
            ? Sizes.Format((long)p.Value, C)
            : p.Value < 0.5 ? _loc.F("gui.home.percent", "<1") : Percent(p.Value))).ToList();
    }

    private SpecFigures ComputeSpecs()
    {
        if (_specsRead is not { } s) return new SpecFigures(Dash, Dash, Dash, Dash);
        var clock = s.CpuMhz is { } mhz ? (mhz / 1000.0).ToString("0.00", C) + " GHz" : null;
        var cores = (s.Cores, s.Threads) switch
        {
            ({ } c, { } t) => _loc.F("gui.home.spec.cores-threads", c, t),
            ({ } c, null) => _loc.F("gui.home.spec.cores", c),
            (null, { } t) => _loc.F("gui.home.spec.threads", t),
            _ => null,
        };
        var ram = (s.RamInstalledBytes, s.RamUsableBytes) switch
        {
            ({ } installed, { } usable) => _loc.F("gui.home.spec.ram", Gb(installed), Gb(usable)),
            ({ } installed, null) => Gb(installed),
            (null, { } usable) => _loc.F("gui.home.spec.ram.usable", Gb(usable)),
            _ => null,
        };
        return new SpecFigures(
            Lines(Join(Tidy(s.CpuName), clock), cores) ?? Dash,
            GpuNames("\n") ?? Dash,
            ram ?? Dash,
            Join(string.IsNullOrWhiteSpace(s.DiskModel) ? null : s.DiskModel.Trim(),
                s.DiskBytes is { } d ? DiskSize(d) : null) ?? Dash);
    }

    private static string? Join(params string?[] parts) =>
        parts.Where(p => p is { Length: > 0 }).ToList() is { Count: > 0 } some ? string.Join(Sep, some) : null;

    private static string? Lines(params string?[] lines) =>
        lines.Where(l => l is { Length: > 0 }).ToList() is { Count: > 0 } some ? string.Join("\n", some) : null;

    /// Memory in binary GB with one decimal, whole when it is whole: "32 GB",
    /// "31,8 GB".
    private string Gb(long bytes)
    {
        var g = Math.Round(bytes / (double)(1L << 30), 1, MidpointRounding.AwayFromZero);
        return (g % 1 == 0 ? g.ToString("0", C) : g.ToString("0.0", C)) + " GB";
    }

    /// A disk as it is sold: decimal TB or GB ("1 TB", "512 GB").
    private string DiskSize(long bytes)
    {
        if (bytes >= 1_000_000_000_000)
        {
            var t = Math.Round(bytes / 1e12, 1, MidpointRounding.AwayFromZero);
            return (t % 1 == 0 ? t.ToString("0", C) : t.ToString("0.0", C)) + " TB";
        }
        return Whole(bytes / 1e9) + " GB";
    }

    /// A hardware name without the trademark marks and the clock Windows
    /// appends: "Intel(R) Core(TM) i7-10750H CPU @ 2.60GHz" → "Intel Core i7-10750H".
    internal static string? Tidy(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var t = Regex.Replace(name, @"\((R|TM|C)\)", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"(\s+CPU)?\s*@.*$", "");
        t = Regex.Replace(t, @"\s+Processor\s*$", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"\s{2,}", " ").Trim();
        return t.Length == 0 ? null : t;
    }

    // ---- the strip

    private HomeStrip ComputeStrip()
    {
        var s = _session;
        var runAll = _loc["gui.button.run-all"];
        var items = s.Items.ToList();
        switch (s.State)
        {
            case SessionState.Looking:
                return new HomeStrip("", _loc["gui.state.looking.start"], Tone.Normal, runAll, false, HomeButton.RunAll, false);
            case SessionState.Running:
            {
                var row = s.CurrentRowId is { } id
                    ? items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase))
                    : null;
                var title = row is null ? null : row.TitleKey is null ? row.Title : _loc.Title(row.TitleKey, row.Title);
                // The row that holds the component store runs DISM: minutes, said up front.
                var doing = row is null ? _loc["gui.button.running"]
                    : row.LinkIds.Contains("component-store") ? _loc.F("gui.state.running.slow", title!)
                    : _loc.F("gui.state.running", title!);
                return new HomeStrip("", doing, Tone.Normal, null, false, HomeButton.None, false);
            }
            case SessionState.Done:
                return DoneStrip(items);
            case SessionState.Failed:
                return new HomeStrip("", _loc.F("gui.state.failed", s.Error ?? ""), Tone.Bad,
                    _loc["gui.button.retry"], true, HomeButton.Retry, false);
        }

        // Ready. What Hepsini yap takes: Yer aç at MIN, the default Speed and
        // Privacy rows, minus skips (ActionSession.InRunAll; ruling R4).
        var taken = items.Count(s.InRunAll) > 0;
        // A failed undo is followed by a rescan: the resting strip has no Geri
        // al (ruling R31); the Speed and Privacy pages keep it for that run.
        if (s.UndoError is { } undoError)
            return new HomeStrip("", _loc.F("gui.state.undo-error", undoError), Tone.Bad, runAll, taken,
                HomeButton.RunAll, false);
        if (s.UndoResults is { } undone)
        {
            var back = _loc.F("gui.state.undone", undone.Count(r => r.Outcome == RowOutcome.Done));
            var notBack = undone.Count(r => r.Outcome == RowOutcome.Failed);
            if (notBack > 0) back += Sep + _loc.F("gui.state.undone.failed", notBack);
            return new HomeStrip("", back, notBack > 0 ? Tone.Warn : Tone.Good, runAll, taken, HomeButton.RunAll, false);
        }
        if (!taken) return new HomeStrip("", _loc["gui.state.nothing"], Tone.Good, null, false, HomeButton.None, false);
        // Apps and settings as the pages count them (ruling R34): a Speed setting is a setting.
        var (apps, settings) = RowWords.Count(items.Where(s.InRunAll));
        return new HomeStrip(_loc["gui.home.strip.ready"],
            string.Join(Sep, Sizes.Format(s.LevelBytes(SpaceLevel.Min), C),
                RowWords.Apps(apps, _loc), RowWords.Settings(settings, _loc)),
            Tone.Normal, runAll, true, HomeButton.RunAll, false);
    }

    /// "Son işlem: 45 GB · 9 uygulama · 7 ayar": the groups the run held, what
    /// went through in each (a setting Windows put back does not count), the
    /// rows that failed, a restart some rows wait for, and the caches left
    /// until their apps close. Geri al only while the run can be undone, which
    /// reverses its Speed and Privacy settings alone (ruling R21); Tekrar tara
    /// always, and the look it starts brings the resting strip back.
    private HomeStrip DoneStrip(IReadOnlyList<ActionItem> items)
    {
        var s = _session;
        var (apps, settings) = RowWords.Count(items.DistinctBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .Where(i => s.Results.TryGetValue(i.Id, out var r) && RowWords.Succeeded(r)));

        var parts = new List<string>();
        if (s.RunGroup is null or ActionGroupKind.Space)
            parts.Add(Sizes.Format(s.Receipt?.FreedBytes ?? s.Results.Values.Sum(r => r.BytesFreed), C));
        if (apps > 0) parts.Add(RowWords.Apps(apps, _loc));
        if (settings > 0) parts.Add(RowWords.Settings(settings, _loc));
        var failed = s.Results.Values.Count(r => r.Outcome == RowOutcome.Failed);
        if (failed > 0) parts.Add(_loc.F("gui.state.done.failed", failed));
        if (s.Results.Values.Any(r => r.Outcome == RowOutcome.PendingRestart))
            parts.Add(_loc["gui.state.done.restart"]);
        if (s.Receipt is { DeferredBytes: > 0 } receipt && SafeDeferredApps() is { Count: > 0 } waitingFor)
            parts.Add(_loc.F("gui.state.done.deferred", Sizes.Format(receipt.DeferredBytes, C), string.Join(", ", waitingFor)));
        if (parts.Count == 0) parts.Add(_loc["action.result.done"]);
        return new HomeStrip(_loc["gui.home.strip.last"], string.Join(Sep, parts), failed > 0 ? Tone.Warn : Tone.Normal,
            _loc["gui.button.rescan"], true, HomeButton.Rescan, s.CanUndo);
    }

    /// The apps whose caches wait for them to close; none when unreadable.
    private IReadOnlyList<string> SafeDeferredApps()
    {
        try { return _backend.DeferredApps(); }
        catch (Exception) { return Array.Empty<string>(); }
    }

    private void OnPrimary()
    {
        switch (Strip.ButtonAction)
        {
            case HomeButton.RunAll:
                // No confirmation before deleting: Hepsini yap runs at once.
                _ = _session.RunAllAsync();
                break;
            case HomeButton.Retry:
            case HomeButton.Rescan:
                _ = _session.RescanAsync();
                break;
        }
    }

    private void OnOpenFailed()
    {
        var items = _session.Items.GroupBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var failed = _session.Results.Values.Where(r => r.Outcome == RowOutcome.Failed)
            .Select(r => items.TryGetValue(r.RowId, out var i) ? i.Group : (ActionGroupKind?)null)
            .OfType<ActionGroupKind>()
            .OrderBy(g => g)
            .Cast<ActionGroupKind?>()
            .FirstOrDefault();
        if (failed is { } group) _open(group);
    }
}
