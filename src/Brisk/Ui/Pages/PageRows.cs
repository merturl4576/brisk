using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;

namespace Brisk.Ui.Pages;

/// One page's rows of one group, kept in step with the session, what of
/// them ran, and the page's one button: run the group, a new look once a
/// run is over, or nothing. Shared by Yer aç, Hızlandır and Gizlilik.
internal sealed class PageRows
{
    private readonly ActionSession _session;
    private readonly ActionGroupKind _group;
    private readonly Loc _loc;
    private readonly Func<IReadOnlyList<string>> _deferredApps;

    public PageRows(ActionSession session, ActionGroupKind group, Loc loc, Func<IReadOnlyList<string>> deferredApps)
    {
        _session = session;
        _group = group;
        _loc = loc;
        _deferredApps = deferredApps;
    }

    public ObservableCollection<RowViewModel> Rows { get; } = new();

    public IEnumerable<ActionItem> Items() => _session.Items.Where(i => i.Group == _group);

    public IEnumerable<ActionItem> Selected() => Items().Where(_session.WillRun);

    public IEnumerable<ActionItem> Ran() => Items().Where(i => _session.Results.ContainsKey(i.Id));

    public IEnumerable<ActionItem> Succeeded() =>
        Items().Where(i => _session.Results.TryGetValue(i.Id, out var r) && RowWords.Succeeded(r));

    /// `ok`: the page's run went through ("✓ Tamam"), and the button rests.
    public bool CanRun(bool ok)
    {
        if (_session.Busy) return false;
        return _session.State switch
        {
            SessionState.Ready => Selected().Any(),
            // A run is over: a fresh look, unless this page's run went through.
            SessionState.Done => !ok,
            SessionState.Failed => true,
            _ => false,
        };
    }

    /// No confirmation before deleting: the page runs at once.
    public void Run(bool ok)
    {
        if (!CanRun(ok)) return;
        _ = _session.State == SessionState.Ready ? _session.RunAsync(_group) : _session.RescanAsync();
    }

    /// Same ids in the same order: refresh in place. Otherwise rebuild, keeping
    /// the instances (and their expanded state) of rows that stayed.
    public void Sync()
    {
        var items = Items().ToList();
        if (items.Select(i => i.Id).SequenceEqual(Rows.Select(r => r.Id)))
        {
            for (var i = 0; i < items.Count; i++)
                if (!ReferenceEquals(Rows[i].Item, items[i])) Rows[i].Replace(items[i]);
                else Rows[i].Refresh();
            return;
        }
        var old = Rows.ToDictionary(r => r.Id);
        Rows.Clear();
        foreach (var item in items)
        {
            if (old.TryGetValue(item.Id, out var row)) row.Replace(item);
            else row = new RowViewModel(item, _session, _loc, _deferredApps);
            Rows.Add(row);
        }
    }
}
