using System;
using System.Threading.Tasks;
using Brisk.Services;

namespace Brisk.ViewModels;

/// Reports synchronously on the calling thread. Progress<T> would post to a
/// captured SynchronizationContext, which unit tests do not have.
public sealed class DelegateProgress : IProgress<string>
{
    private readonly Action<string> _handler;
    public DelegateProgress(Action<string> handler) { _handler = handler; }
    public void Report(string value) => _handler(value);
}

/// Health score → theme brush key, shared by the health page and the flyout.
public static class HealthBrush
{
    public static string KeyFor(int health) =>
        health >= 90 ? "Good"
        : health >= 70 ? "SeverityWarning"
        : "SeverityCritical";
}

/// The one shared scan state. Every page and the flyout render from here.
public sealed class AppState : ViewModelBase
{
    private readonly IEngineHost _host;
    private ScanSnapshot? _snapshot;
    private bool _isScanning;
    private bool _isCleaning;
    private string _progressText = "";

    public AppState(IEngineHost host) { _host = host; }

    public ScanSnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }

    /// Round-13 re-review (N1): the sibling of IsScanning. One runner sits
    /// behind three clean buttons, so while ANY surface is cleaning the
    /// other two would be refused the lease — silently, since each view
    /// model's busy flag only knows about its own button. Every clean
    /// button binds here, so the refusal is visible instead of swallowed.
    public bool IsCleaning { get => _isCleaning; private set => Set(ref _isCleaning, value); }

    /// Wired once at composition: the runner owns the lease, this owns the
    /// signal the UI binds to. Nothing else may set it — a flag that could
    /// drift from the lease would disable buttons for a clean that ended.
    public void TrackCleaning(SafeCleanRunner runner)
        => runner.RunningChanged += running => IsCleaning = running;
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }

    public event Action? Changed;

    public async Task ScanAsync()
    {
        if (IsScanning) return;
        IsScanning = true;                    // set before the first await — re-entry guard
        await Task.Yield();
        try
        {
            Snapshot = await _host.ScanAsync(new DelegateProgress(m => ProgressText = m));
        }
        finally
        {
            IsScanning = false;
        }
        Changed?.Invoke();
    }
}
