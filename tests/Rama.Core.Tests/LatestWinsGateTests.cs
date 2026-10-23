using Rama.Core.Polling;

namespace Rama.Core.Tests;

public class LatestWinsGateTests
{
    [Fact]
    public async Task Runs_NeverOverlap_AndSupersededRequestsAreSkipped()
    {
        var gate = new LatestWinsGate();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maxRunning = 0;
        var executed = new List<int>();

        Func<int, Func<Task>> job = id => async () =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            executed.Add(id);
            if (id == 1) { started.SetResult(); await release.Task; }
            Interlocked.Decrement(ref running);
        };

        var first = gate.RunAsync(job(1));
        await started.Task;
        var second = gate.RunAsync(job(2));
        var third = gate.RunAsync(job(3));
        release.SetResult();
        await Task.WhenAll(first, second, third);

        Assert.Equal([1, 3], executed);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task FailingAction_ReleasesGate_AndPropagates()
    {
        var gate = new LatestWinsGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(() => throw new InvalidOperationException()));
        var ran = false;
        await gate.RunAsync(() => { ran = true; return Task.CompletedTask; });
        Assert.True(ran);
        await Assert.ThrowsAsync<ArgumentNullException>(() => gate.RunAsync(null!));
    }
}
