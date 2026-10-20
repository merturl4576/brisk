using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using BriskEngine.Actions;
using Brisk.Localization;
using Brisk.Session;
using Brisk.ViewModels;

namespace Brisk.Ui.Pages;

/// One of the three detail pages: its group's rows in plan order, a footer
/// with what is ticked, and "Do these" for this group alone. The ticks are
/// the session's, shared with Home.
public sealed class GroupPageViewModel : ViewModelBase
{
    private readonly ActionSession _session;
    private readonly Loc _loc;
    private readonly Action<IReadOnlyList<ActionItem>, ActionGroupKind?> _confirm;
    private readonly Func<IReadOnlyList<string>> _deferredApps;
    private readonly string _key;

    public GroupPageViewModel(ActionSession session, ActionGroupKind group, Loc loc,
        Action<IReadOnlyList<ActionItem>, ActionGroupKind?> confirm, Func<IReadOnlyList<string>> deferredApps)
    {
        _session = session;
        Group = group;
        _loc = loc;
        _confirm = confirm;
        _deferredApps = deferredApps;
        _key = group.ToString().ToLowerInvariant();
        RunGroup = new RelayCommand(OnRun, () => _session.State == SessionState.Ready && !_session.Busy && Ticked().Any());
        Sync();
        session.Changed += OnChanged;
        loc.PropertyChanged += OnLanguage;
    }

    public ActionGroupKind Group { get; }
    public Loc L => _loc;
    public string Title => _loc[$"gui.page.{_key}.title"];
    public string Header => _loc[$"gui.page.{_key}.header"];

    public ObservableCollection<RowViewModel> Rows { get; } = new();

    /// The live panels the shell places above and below the rows.
    private IReadOnlyList<object> _above = Array.Empty<object>(), _below = Array.Empty<object>();
    public IReadOnlyList<object> Above { get => _above; set => Set(ref _above, value); }
    public IReadOnlyList<object> Below { get => _below; set => Set(ref _below, value); }

    public RelayCommand RunGroup { get; }

    public string Footer
    {
        get
        {
            var ticked = Ticked().ToList();
            if (Group != ActionGroupKind.Space) return _loc.F("gui.footer.count", ticked.Count);
            return _loc.F("gui.footer.count-bytes", ticked.Count,
                Sizes.Format(ticked.Sum(_session.SelectedBytes), _loc.Culture));
        }
    }

    public void Expand(string rowId)
    {
        if (Rows.FirstOrDefault(r => r.Id == rowId) is { } row) row.IsExpanded = true;
    }

    private IEnumerable<ActionItem> Items() => _session.Items.Where(i => i.Group == Group);

    private IEnumerable<ActionItem> Ticked() => Items().Where(_session.WillRun);

    private void OnRun()
    {
        var unrecoverable = _session.NeedsConfirmation(Group);
        if (unrecoverable.Count > 0) _confirm(unrecoverable, Group);
        else _ = _session.RunAsync(Group);
    }

    private void OnChanged()
    {
        Sync();
        Raise(nameof(Footer));
        RunGroup.RaiseCanExecuteChanged();
    }

    private void OnLanguage(object? sender, PropertyChangedEventArgs e)
    {
        Raise(nameof(Title));
        Raise(nameof(Header));
        Raise(nameof(Footer));
        foreach (var r in Rows) r.Refresh();
    }

    /// Same ids in the same order: refresh in place. Otherwise rebuild, keeping
    /// the instances (and their expanded state) of rows that stayed.
    private void Sync()
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
