namespace Rama.Core.Polling;

// Serializa operaciones asíncronas (p. ej. reconexiones): nunca corren en paralelo y, si llegan varias
// mientras una está en curso, solo se ejecuta la más reciente (las intermedias se descartan).
public sealed class LatestWinsGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _generation;

    public async Task RunAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var mine = Interlocked.Increment(ref _generation);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (mine != Volatile.Read(ref _generation)) return;
            await action().ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
