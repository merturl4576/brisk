using System;
using System.Threading.Tasks;
using Brisk.Localization;
using Brisk.Services;
using Brisk.ViewModels;
using Xunit;

namespace Brisk.Tests;

/// ROUND 13 re-review: the runner's lease is the app-wide "one bin mutation
/// at a time" contract, and AppState.IsCleaning is how the UI shows it.
/// Both are pinned here, away from any one surface.
public class SafeCleanRunnerTests
{
    private static (SafeCleanRunner Runner, AppState State, FakeEngineHost Host) Build()
    {
        var host = new FakeEngineHost();
        var runner = new SafeCleanRunner(new CleanService(host, new Settings()), new FakeBin());
        var state = new AppState(host);
        state.TrackCleaning(runner);
        return (runner, state, host);
    }

    /// N1: every clean button disables on this signal, so it has to mirror
    /// the lease exactly — a flag left standing after a clean would leave
    /// the whole app unable to clean until it restarts.
    [Fact]
    public void IsCleaning_MirrorsTheLease()
    {
        var (runner, state, _) = Build();
        Assert.False(state.IsCleaning);

        var lease = runner.TryBegin();
        Assert.NotNull(lease);
        Assert.True(state.IsCleaning);
        Assert.Null(runner.TryBegin());          // refused while held

        lease!.Dispose();
        Assert.False(state.IsCleaning);

        // A stale second Dispose must not release — or un-signal — the lease
        // a LATER clean is holding.
        var second = runner.TryBegin();
        Assert.NotNull(second);
        lease.Dispose();
        Assert.True(state.IsCleaning);
        Assert.Null(runner.TryBegin());
        second!.Dispose();
        Assert.False(state.IsCleaning);
    }

    /// minor 12: the lease is the TOKEN, not a flag to check. "Somebody
    /// holds it" would wave through exactly the case worth catching — a
    /// second surface running while the first still holds it.
    [Fact]
    public async Task RunAsync_WithoutThisRunnersLiveLease_Throws()
    {
        var (runner, _, host) = Build();
        var scan = host.NextSnapshot.Cleaner;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(new DummyLease(), scan));

        // Another runner's lease is not this runner's permission.
        var (other, _, _) = Build();
        using var foreign = other.TryBegin();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(foreign!, scan));

        // A released lease is spent, even while the runner sits idle.
        var spent = runner.TryBegin();
        spent!.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(spent, scan));

        // THE case the old "somebody holds it" guard passed: the runner is
        // busy, and a second caller arrives without its lease.
        using var mine = runner.TryBegin();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(new DummyLease(), scan));
    }

    private sealed class DummyLease : IDisposable
    {
        public void Dispose() { }
    }
}
