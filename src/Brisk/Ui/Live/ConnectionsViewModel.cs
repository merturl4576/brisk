using System;
using System.Collections.Generic;
using System.Linq;
using BriskEngine.Live;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Live;

public sealed record ConnectionLine(string Text, bool Telemetry);

/// "Talking to now" on the privacy page: who each app is connected to,
/// refreshed every two seconds while the page is open.
public sealed class ConnectionsViewModel : ViewModelBase
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(2);
    public const int MaxRows = 15;

    private readonly Action<Action> _post;
    private readonly Action<Action> _background;
    private readonly IActionBackend _backend;
    private readonly IUiTimers _timers;
    private readonly Loc _loc;
    private IDisposable? _ticker;
    private bool _reading;
    private ConnectionSnapshot? _last;

    public ConnectionsViewModel(Action<Action> post, Action<Action> background, IActionBackend backend,
        IUiTimers timers, Loc loc)
    {
        _post = post;
        _background = background;
        _backend = backend;
        _timers = timers;
        _loc = loc;
        loc.PropertyChanged += (_, _) =>
        {
            Raise(nameof(L));
            if (_last is not null) Rows = Lines(_last);
        };
    }

    public Loc L => _loc;

    private IReadOnlyList<ConnectionLine> _rows = Array.Empty<ConnectionLine>();
    public IReadOnlyList<ConnectionLine> Rows { get => _rows; private set => Set(ref _rows, value); }

    private bool _dnsRead = true;
    public bool DnsRead { get => _dnsRead; private set => Set(ref _dnsRead, value); }

    private string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public void Activate()
    {
        if (_ticker is not null) return;
        _ticker = _timers.Every(Every, Read);
        Read();
    }

    public void Deactivate()
    {
        _ticker?.Dispose();
        _ticker = null;
    }

    private IReadOnlyList<ConnectionLine> Lines(ConnectionSnapshot s) => s.Rows.Take(MaxRows).Select(r =>
    {
        var owner = r.IsLocal || r.Owner == ConnectionReader.LocalOwner ? _loc["action.connections.local"]
            : r.Owner == ConnectionReader.UnknownOwner ? _loc["action.connections.unknown"]
            : r.Owner;
        var host = r.Host is null ? "" : $" ({r.Host})";
        return new ConnectionLine($"{r.App} → {owner}{host} · {r.Count}", r.IsTelemetry);
    }).ToList();

    private void Read()
    {
        if (_reading) return;
        _reading = true;
        _background(() =>
        {
            ConnectionSnapshot? snap = null;
            try { snap = _backend.ReadConnections(); }
            catch (Exception) { }
            _post(() =>
            {
                _reading = false;
                if (snap is null)
                {
                    Error = _loc["action.unread"];
                    return;
                }
                Error = null;
                _last = snap;
                DnsRead = snap.DnsRead;
                Rows = Lines(snap);
            });
        });
    }
}

public sealed record UsageLine(string App, string Sent, string Received);

/// "Sent in 30 days": read once, the first time the privacy page opens.
public sealed class UsageViewModel : ViewModelBase
{
    private readonly Action<Action> _post;
    private readonly Action<Action> _background;
    private readonly IActionBackend _backend;
    private readonly Loc _loc;
    private bool _started;
    private IReadOnlyList<UsageRow>? _last;

    public UsageViewModel(Action<Action> post, Action<Action> background, IActionBackend backend, Loc loc)
    {
        _post = post;
        _background = background;
        _backend = backend;
        _loc = loc;
        loc.PropertyChanged += (_, _) =>
        {
            Raise(nameof(L));
            if (_last is not null) Rows = Lines(_last);
        };
    }

    public Loc L => _loc;

    private IReadOnlyList<UsageLine> _rows = Array.Empty<UsageLine>();
    public IReadOnlyList<UsageLine> Rows { get => _rows; private set => Set(ref _rows, value); }

    private bool _reading;
    public bool Reading { get => _reading; private set => Set(ref _reading, value); }

    private string? _error;
    public string? Error { get => _error; private set => Set(ref _error, value); }

    public void Activate()
    {
        if (_started) return;
        _started = true;
        Reading = true;
        _background(() =>
        {
            IReadOnlyList<UsageRow>? rows = null;
            try { rows = _backend.ReadUsage(); }
            catch (Exception) { }
            _post(() =>
            {
                Reading = false;
                if (rows is null)
                {
                    Error = _loc["action.unread"];
                    return;
                }
                _last = rows;
                Rows = Lines(rows);
            });
        });
    }

    private IReadOnlyList<UsageLine> Lines(IReadOnlyList<UsageRow> rows) => rows.Select(r => new UsageLine(
        r.App == NetworkUsageReader.WindowsSystem ? _loc["action.usage.windows"] : r.App,
        _loc.F("gui.usage.sent", Sizes.Format(r.BytesSent, _loc.Culture)),
        _loc.F("gui.usage.received", Sizes.Format(r.BytesReceived, _loc.Culture)))).ToList();
}
