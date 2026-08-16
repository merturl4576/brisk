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
    private string _progressText = "";

    public AppState(IEngineHost host) { _host = host; }

    public ScanSnapshot? Snapshot { get => _snapshot; private set => Set(ref _snapshot, value); }
    public bool IsScanning { get => _isScanning; private set => Set(ref _isScanning, value); }
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
