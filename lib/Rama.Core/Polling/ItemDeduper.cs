namespace Rama.Core.Polling;

// Emite solo ids nuevos respecto del sondeo anterior; el snapshot permite persistir entre sesiones.
public sealed class ItemDeduper<TKey> where TKey : notnull
{
    private readonly object _gate = new();
    private HashSet<TKey> _seen;

    public ItemDeduper(IEnumerable<TKey>? snapshot = null) => _seen = new HashSet<TKey>(snapshot ?? []);

    public IReadOnlyList<TKey> Filter(IEnumerable<TKey> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = new HashSet<TKey>(current);
        lock (_gate)
        {
            var fresh = next.Where(k => !_seen.Contains(k)).ToList();
            _seen = next;
            return fresh;
        }
    }

    public IReadOnlyCollection<TKey> Snapshot()
    {
        lock (_gate) return _seen.ToArray();
    }
}
