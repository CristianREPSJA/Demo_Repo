using Rama.Core.Http;

namespace Rama.Core.Polling;

public sealed record PollerOptions(TimeSpan Interval, TimeSpan MaxBackoff);

// Sondeo periódico con backoff exponencial ante fallos y pausa tras error de autenticación.
public sealed class Poller : IAsyncDisposable
{
    private readonly Func<CancellationToken, Task> _tick;
    private readonly PollerOptions _options;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private PeriodicTimer? _timer;
    private TaskCompletionSource? _resume;

    public Poller(Func<CancellationToken, Task> tick, PollerOptions options, TimeProvider? time = null)
    {
        if (options.Interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        _tick = tick ?? throw new ArgumentNullException(nameof(tick));
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public bool IsRunning => _loop is { IsCompleted: false };
    public bool IsPaused { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public TimeSpan CurrentInterval { get; private set; }
    public Exception? LastError { get; private set; }

    // Se dispara al terminar cada ciclo (ya con el estado actualizado).
    public event Action? TickCompleted;

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning) return;
            _cts = new CancellationTokenSource();
            CurrentInterval = _options.Interval;
            _timer = new PeriodicTimer(CurrentInterval, _time);
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? loop;
        lock (_gate)
        {
            loop = _loop;
            _cts?.Cancel();
            _resume?.TrySetCanceled();
        }

        if (loop is not null)
        {
            try { await loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        lock (_gate)
        {
            _timer?.Dispose();
            _cts?.Dispose();
            (_timer, _cts, _loop) = (null, null, null);
            IsPaused = false;
        }
    }

    // Reanuda tras una pausa por autenticación (p. ej. tras renovar el token).
    public void Resume()
    {
        lock (_gate)
        {
            if (!IsPaused) return;
            _resume?.TrySetResult();
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            if (!first && !await _timer!.WaitForNextTickAsync(ct).ConfigureAwait(false)) break;
            first = false;

            var paused = await ExecuteTickAsync(ct).ConfigureAwait(false);
            TickCompleted?.Invoke();
            if (paused) await WaitForResumeAsync(ct).ConfigureAwait(false);
        }
    }

    // Devuelve true si el ciclo terminó en error de autenticación (pausa).
    private async Task<bool> ExecuteTickAsync(CancellationToken ct)
    {
        try
        {
            await _tick(ct).ConfigureAwait(false);
            ConsecutiveFailures = 0;
            LastError = null;
            SetInterval(_options.Interval);
            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiAuthException ex)
        {
            LastError = ex;
            lock (_gate)
            {
                _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                IsPaused = true;
            }
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex;
            ConsecutiveFailures++;
            var factor = Math.Pow(2, Math.Min(ConsecutiveFailures, 30));
            var ticks = Math.Min(_options.Interval.Ticks * factor, _options.MaxBackoff.Ticks);
            SetInterval(TimeSpan.FromTicks((long)Math.Max(ticks, _options.Interval.Ticks)));
            return false;
        }
    }

    private async Task WaitForResumeAsync(CancellationToken ct)
    {
        Task wait;
        lock (_gate) wait = _resume!.Task;
        await wait.WaitAsync(ct).ConfigureAwait(false);
        lock (_gate)
        {
            IsPaused = false;
            ConsecutiveFailures = 0;
            // Timer nuevo: descarta ticks acumulados durante la pausa.
            _timer?.Dispose();
            CurrentInterval = _options.Interval;
            _timer = new PeriodicTimer(CurrentInterval, _time);
        }
        // Tras reanudar se ejecuta un ciclo inmediato: el bucle vuelve a esperar el timer, así que se fuerza aquí.
        var paused = await ExecuteTickAsync(ct).ConfigureAwait(false);
        TickCompleted?.Invoke();
        if (paused) await WaitForResumeAsync(ct).ConfigureAwait(false);
    }

    private void SetInterval(TimeSpan interval)
    {
        if (interval == CurrentInterval) return;
        CurrentInterval = interval;
        _timer!.Period = interval;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
