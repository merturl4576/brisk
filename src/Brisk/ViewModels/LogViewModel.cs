using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Brisk.Services;
using BriskEngine.Diagnostics;
using BriskEngine.Logging;

namespace Brisk.ViewModels;

public sealed class UndoableRow
{
    public UndoableRow(UndoableFix fix, System.Func<UndoableRow, Task> undo)
    {
        RuleId = fix.RuleId;
        WhenText = fix.FixedAtUtc.ToLocalTime().ToString("dd.MM HH:mm");
        UndoCommand = new RelayCommand(() => _ = undo(this));
    }

    public string RuleId { get; }
    public string WhenText { get; }
    public RelayCommand UndoCommand { get; }
}

public sealed class LogViewModel : ViewModelBase
{
    private readonly AppState _state;
    private readonly IEngineHost _host;
    private readonly System.Func<bool> _isDryRun;

    public LogViewModel(AppState state, IEngineHost host, System.Func<bool> isDryRun)
    {
        _state = state;
        _host = host;
        _isDryRun = isDryRun;
        state.Changed += Refresh;
    }

    public ObservableCollection<UndoableRow> Undoables { get; } = new();
    public ObservableCollection<ActionLogEntry> Entries { get; } = new();

    public async Task UndoAsync(UndoableRow row)
    {
        if (_isDryRun()) return;   // dry run: report only, no message surface here
        await Task.Run(() => _host.Undo(row.RuleId));
        await _state.ScanAsync();   // Changed handler refreshes both lists
    }

    private void Refresh()
    {
        Undoables.Clear();
        foreach (var fix in _host.ListUndoable())
            Undoables.Add(new UndoableRow(fix, UndoAsync));
        Entries.Clear();
        foreach (var entry in _host.ReadLog())
            Entries.Add(entry);
    }
}
