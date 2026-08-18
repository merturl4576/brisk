using System;
using System.Threading;
using System.Threading.Tasks;

namespace Brisk.Services;

/// A display mode change is the one fix whose failure also removes the user's
/// ability to undo it: a driver can advertise a rate the cable or adapter
/// cannot carry, and the screen goes black. So the change is provisional —
/// unless it is confirmed inside the window, it rolls back on its own.
public sealed class RefreshConfirmation
{
    private readonly Action _rollback;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _kept = new();

    // One confirmation per applied change: the wait — and the rollback it can
    // trigger — must run at most once per instance, even if a caller invokes
    // AwaitConfirmationAsync again or concurrently.
    private bool _answered;

    public RefreshConfirmation(Action rollback,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _rollback = rollback;
        _delay = delay ?? Task.Delay;
    }

    public TimeSpan Window { get; init; } = TimeSpan.FromSeconds(15);

    public bool RolledBack { get; private set; }

    /// True when the user confirmed the picture is back; false when the window
    /// elapsed and the prior mode was restored.
    public async Task<bool> AwaitConfirmationAsync()
    {
        if (_answered)
        {
            return !RolledBack;
        }
        _answered = true;

        try
        {
            await _delay(Window, _kept.Token);
        }
        catch (OperationCanceledException)
        {
            return true;
        }
        _rollback();
        RolledBack = true;
        return false;
    }

    public void Keep() => _kept.Cancel();
}
