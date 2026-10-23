using System.Net;
using Rama.Core.Http;
using Rama.Core.Notifications;
using Rama.Core.Polling;
using Rama.Core.Tests.Support;

namespace Rama.Core.Tests;

public class ItemDeduperTests
{
    [Fact]
    public void EmitsOnlyNewIdsSinceLastPoll()
    {
        var d = new ItemDeduper<int>();
        Assert.Equal([1, 2], d.Filter([1, 2]));
        Assert.Equal([3], d.Filter([1, 2, 3]));
        Assert.Empty(d.Filter([1, 2, 3]));
    }

    [Fact]
    public void ForgetsDisappearedIds_SoReappearingIsNew()
    {
        var d = new ItemDeduper<string>();
        d.Filter(["a", "b"]);
        d.Filter(["a"]);
        Assert.Equal(["b"], d.Filter(["a", "b"]));
    }

    [Fact]
    public void SnapshotRestore_PreventsReEmission()
    {
        var d = new ItemDeduper<long>();
        d.Filter([10, 20]);
        var restored = new ItemDeduper<long>(d.Snapshot());
        Assert.Equal([30], restored.Filter([10, 20, 30]));
        Assert.Throws<ArgumentNullException>(() => d.Filter(null!));
    }
}

public class NotifierTests
{
    [Fact]
    public async Task NullNotifier_DoesNothing()
    {
        await NullNotifier.Instance.NotifyAsync(new NotificationMessage("1", "t", "b"));
        Assert.Null(new NotificationMessage("1", "t", "b").ActionUrl);
    }
}

public class PollerTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly PollerOptions Opts = new(Interval, TimeSpan.FromSeconds(300));

    private sealed class Harness : IAsyncDisposable
    {
        public FakeTimeProvider Time { get; } = new();
        public Poller Poller { get; }
        public int Calls;
        public Func<int, Exception?> Plan { get; set; } = _ => null;
        private readonly SemaphoreSlim _done = new(0);

        public Harness()
        {
            Poller = new Poller(_ =>
            {
                var ex = Plan(++Calls);
                return ex is null ? Task.CompletedTask : Task.FromException(ex);
            }, Opts, Time);
            Poller.TickCompleted += () => _done.Release();
        }

        public async Task WaitTick() =>
            Assert.True(await _done.WaitAsync(TimeSpan.FromSeconds(5)), "tick no llegó");

        public async Task AdvanceAndWait(TimeSpan by)
        {
            Time.Advance(by);
            await WaitTick();
        }

        public ValueTask DisposeAsync() => Poller.DisposeAsync();
    }

    [Fact]
    public async Task RunsImmediatelyThenEveryInterval()
    {
        await using var h = new Harness();
        h.Poller.Start();
        h.Poller.Start(); // idempotente
        await h.WaitTick();
        Assert.Equal(1, h.Calls);
        Assert.True(h.Poller.IsRunning);
        await h.AdvanceAndWait(Interval);
        await h.AdvanceAndWait(Interval);
        Assert.Equal(3, h.Calls);
        Assert.Equal(0, h.Poller.ConsecutiveFailures);
    }

    [Fact]
    public async Task FailuresBackOffExponentiallyUpToMaxThenReset()
    {
        await using var h = new Harness();
        h.Plan = n => n <= 5 ? new InvalidOperationException("x") : null;
        h.Poller.Start();
        await h.WaitTick();
        Assert.Equal(TimeSpan.FromSeconds(120), h.Poller.CurrentInterval);
        await h.AdvanceAndWait(TimeSpan.FromSeconds(120));
        Assert.Equal(TimeSpan.FromSeconds(240), h.Poller.CurrentInterval);
        await h.AdvanceAndWait(TimeSpan.FromSeconds(240));
        Assert.Equal(TimeSpan.FromSeconds(300), h.Poller.CurrentInterval); // tope
        await h.AdvanceAndWait(TimeSpan.FromSeconds(300));
        await h.AdvanceAndWait(TimeSpan.FromSeconds(300));
        Assert.Equal(5, h.Calls);
        Assert.IsType<InvalidOperationException>(h.Poller.LastError);
        await h.AdvanceAndWait(TimeSpan.FromSeconds(300));
        Assert.Equal(Interval, h.Poller.CurrentInterval);
        Assert.Equal(0, h.Poller.ConsecutiveFailures);
        Assert.Null(h.Poller.LastError);
    }

    [Fact]
    public async Task AuthError_PausesUntilResumed()
    {
        await using var h = new Harness();
        h.Plan = n => n == 1 ? new ApiAuthException(HttpStatusCode.Unauthorized, "hint") : null;
        h.Poller.Start();
        await h.WaitTick();
        Assert.True(h.Poller.IsPaused);
        h.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.True(h.Poller.IsPaused); // en pausa el reloj no dispara ciclos

        h.Poller.Resume();
        await h.WaitTick();
        Assert.Equal(2, h.Calls); // un ciclo sondeado en pausa daría 3
        Assert.False(h.Poller.IsPaused);
        h.Poller.Resume(); // sin pausa: no-op
    }

    [Fact]
    public async Task AuthErrorAgainAfterResume_PausesAgain()
    {
        await using var h = new Harness();
        h.Plan = n => n <= 2 ? new ApiAuthException(HttpStatusCode.Forbidden, null) : null;
        h.Poller.Start();
        await h.WaitTick();
        h.Poller.Resume();
        await h.WaitTick();
        Assert.True(h.Poller.IsPaused);
        h.Poller.Resume();
        await h.WaitTick();
        Assert.False(h.Poller.IsPaused);
        Assert.Equal(3, h.Calls);
    }

    [Fact]
    public async Task Stop_EndsLoopAndAllowsRestart()
    {
        await using var h = new Harness();
        h.Poller.Start();
        await h.WaitTick();
        await h.Poller.StopAsync();
        Assert.False(h.Poller.IsRunning);
        h.Time.Advance(Interval * 3);
        Assert.Equal(1, h.Calls);
        await h.Poller.StopAsync(); // idempotente
        h.Poller.Start();
        await h.WaitTick();
        Assert.Equal(2, h.Calls);
    }

    [Fact]
    public async Task Stop_WhilePaused_Completes()
    {
        await using var h = new Harness();
        h.Plan = _ => new ApiAuthException(HttpStatusCode.Unauthorized, null);
        h.Poller.Start();
        await h.WaitTick();
        await h.Poller.StopAsync();
        Assert.False(h.Poller.IsRunning);
        Assert.False(h.Poller.IsPaused);
    }

    [Fact]
    public async Task SubscriberThatThrows_DoesNotKillLoopOrStop()
    {
        await using var h = new Harness();
        var boom = new InvalidOperationException("suscriptor");
        h.Poller.TickCompleted += () => throw boom;
        h.Poller.Start();
        await h.WaitTick();
        Assert.Same(boom, h.Poller.LastError);
        await h.AdvanceAndWait(Interval);
        Assert.Equal(2, h.Calls);
        await h.Poller.StopAsync();
    }

    [Fact]
    public async Task AuthPause_ManyResumes_RunsAsLoopWithoutNesting()
    {
        await using var h = new Harness();
        h.Plan = n => n <= 50 ? new ApiAuthException(HttpStatusCode.Unauthorized, null) : null;
        h.Poller.Start();
        await h.WaitTick();
        for (var i = 0; i < 50; i++)
        {
            h.Poller.Resume();
            await h.WaitTick();
        }
        Assert.Equal(51, h.Calls);
        Assert.False(h.Poller.IsPaused);
    }

    [Fact]
    public void Ctor_Validates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Poller(_ => Task.CompletedTask, new(TimeSpan.Zero, Interval)));
        Assert.Throws<ArgumentNullException>(() => new Poller(null!, Opts));
    }
}
