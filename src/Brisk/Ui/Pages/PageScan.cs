using System;
using System.Collections.Generic;
using System.Linq;
using Brisk.Session;

namespace Brisk.Ui.Pages;

/// The scan a page plays when it comes on screen, shared by Yer aç,
/// Hızlandır and Gizlilik. Rows appear as the look delivers them and are
/// revealed one by one, 300 ms apart, while brisk's moving background runs;
/// otherwise every delivered row shows at once (ruling R30). The reveal is
/// keyed on the row's id, so a row the scan has shown stays shown when the
/// list is rebuilt in another order.
internal sealed class PageScan
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(300);

    private readonly ActionSession _session;
    private readonly IUiTimers? _timers;
    private readonly Func<bool>? _moving;
    private readonly Func<IEnumerable<RowViewModel>> _ordered;
    private readonly Func<IEnumerable<RowViewModel>> _always;
    private readonly Func<bool> _dataIn;
    private readonly Action _stepped;
    private readonly HashSet<string> _revealed = new(StringComparer.OrdinalIgnoreCase);
    private bool _entered;
    private IDisposable? _tick;

    /// `ordered`: the rows the scan reveals, in reveal order. `always`: rows
    /// shown whatever the scan does. `dataIn`: the look has delivered the
    /// page's rows. `stepped`: runs after each reveal step.
    public PageScan(ActionSession session, IUiTimers? timers, Func<bool>? moving,
        Func<IEnumerable<RowViewModel>> ordered, Func<bool> dataIn, Action stepped,
        Func<IEnumerable<RowViewModel>>? always = null)
    {
        _session = session;
        _timers = timers;
        _moving = moving;
        _ordered = ordered;
        _dataIn = dataIn;
        _stepped = stepped;
        _always = always ?? Enumerable.Empty<RowViewModel>;
    }

    public bool Scanning { get; private set; }
    public bool Active { get; private set; }

    /// The reveal and the level-up lights play: timers exist and the moving background runs.
    public bool Moving => _timers is not null && (_moving?.Invoke() ?? true);

    /// Whether the scan's steps have reached this row.
    public bool Reached(RowViewModel row) => _revealed.Contains(row.Id);

    /// The page came on screen: the scan plays, unless a run is going or
    /// over. True when it started one.
    public bool Enter()
    {
        _entered = true;
        var started = _session.State is not (SessionState.Running or SessionState.Done);
        if (started)
        {
            Scanning = true;
            _revealed.Clear();
        }
        Apply();
        Settle();
        return started;
    }

    public void Activate()
    {
        Active = true;
        Settle();
    }

    public void Deactivate()
    {
        Active = false;
        StopTick();
    }

    /// The session's state moved: a new look restarts the scan on a page
    /// that was entered; a run ends it.
    public void OnState(SessionState state)
    {
        if (state == SessionState.Looking && _entered) Start();
        else if (state == SessionState.Running) End();
    }

    /// Not moving: every delivered row at once, the scan over once the data
    /// is in. Moving: the reveal timer runs while the page is on screen.
    public void Settle()
    {
        if (!Scanning) return;
        if (!Moving)
        {
            if (_dataIn()) End();
            return;
        }
        if (_tick is null && Active) _tick = _timers!.Every(Step, Tick);
    }

    public void Apply()
    {
        var all = !Scanning || !Moving;
        foreach (var row in _ordered()) row.Revealed = all || _revealed.Contains(row.Id);
        foreach (var row in _always()) row.Revealed = true;
    }

    private void Start()
    {
        Scanning = true;
        _revealed.Clear();
        Apply();
    }

    private void End()
    {
        Scanning = false;
        StopTick();
        Apply();
    }

    /// One step: the next row in order the scan has not shown; after the
    /// last one, the end of the scan once the data is in. With nothing to
    /// show the timer stops; the next change from the look starts it again.
    private void Tick()
    {
        if (!Scanning)
        {
            StopTick();
            return;
        }
        if (_ordered().FirstOrDefault(r => !_revealed.Contains(r.Id)) is { } next)
        {
            _revealed.Add(next.Id);
            Apply();
        }
        else if (_dataIn()) End();
        else StopTick();
        _stepped();
    }

    private void StopTick()
    {
        _tick?.Dispose();
        _tick = null;
    }
}
