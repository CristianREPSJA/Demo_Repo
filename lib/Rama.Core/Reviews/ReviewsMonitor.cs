using Rama.Core.Notifications;
using Rama.Core.Polling;

namespace Rama.Core.Reviews;

// Sondeo (60 s) del snapshot; notifica solo lo nuevo. La primera carga solo siembra el deduper
// (SUPUESTO: lo ya pendiente al abrir la app no se notifica), salvo notifyInitial.
public sealed class ReviewsMonitor : IAsyncDisposable
{
    public static readonly PollerOptions DefaultOptions = new(TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(10));

    private readonly Func<CancellationToken, Task<ReviewsSnapshot>> _load;
    private readonly INotifier _notifier;
    private readonly ItemDeduper<string> _deduper;
    private readonly bool _notifyInitial;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Poller _poller;
    private bool _seeded;

    public ReviewsMonitor(Func<CancellationToken, Task<ReviewsSnapshot>> load, INotifier notifier,
        PollerOptions? options = null, TimeProvider? time = null, bool notifyInitial = false,
        ItemDeduper<string>? deduper = null)
    {
        _load = load;
        _notifier = notifier;
        _notifyInitial = notifyInitial;
        _deduper = deduper ?? new ItemDeduper<string>();
        _poller = new Poller(ct => RunAsync(rethrow: true, ct), options ?? DefaultOptions, time);
    }

    public ReviewsSnapshot? Snapshot { get; private set; }
    public Exception? LastError { get; private set; }
    public bool IsPaused => _poller.IsPaused;

    // Se dispara tras cada carga (éxito o fallo); Snapshot/LastError ya están actualizados.
    public event Action? Updated;

    public void Start() => _poller.Start();
    public Task StopAsync() => _poller.StopAsync();

    // Refresco manual: nunca lanza; el error queda en LastError. Reanuda el sondeo si estaba en pausa.
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var ok = await RunAsync(rethrow: false, ct).ConfigureAwait(false);
        if (ok) _poller.Resume();
    }

    private async Task<bool> RunAsync(bool rethrow, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var snap = await _load(ct).ConfigureAwait(false);
            Snapshot = snap;
            LastError = null;
            await NotifyNewAsync(snap, ct).ConfigureAwait(false);
            Updated?.Invoke();
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            LastError = ex;
            Updated?.Invoke();
            if (rethrow) throw;
            return false;
        }
        finally { _gate.Release(); }
    }

    private async Task NotifyNewAsync(ReviewsSnapshot snap, CancellationToken ct)
    {
        var pending = snap.Pending.ToList();
        var fresh = _deduper.Filter(pending.Select(i => i.Key)).ToHashSet();
        var first = !_seeded;
        _seeded = true;
        if (first && !_notifyInitial) return;
        foreach (var item in pending.Where(i => fresh.Contains(i.Key)))
            await _notifier.NotifyAsync(ReviewNotifications.For(item), ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await _poller.DisposeAsync().ConfigureAwait(false);
}
