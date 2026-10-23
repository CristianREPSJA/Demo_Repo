namespace Rama.Core.Terminal;

// Una instancia por ruta de repositorio, creada al primer uso y liberada al cerrar la pestaña.
public sealed class RepoScopedCache<T> : IDisposable where T : class
{
    private readonly Func<string, T> _factory;
    private readonly Dictionary<string, T> _items;
    private readonly object _gate = new();

    public RepoScopedCache(Func<string, T> factory, bool ignoreCase = false)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _items = new Dictionary<string, T>(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    }

    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return path.Replace('\\', '/').TrimEnd('/');
    }

    public T GetOrCreate(string path)
    {
        var key = Normalize(path);
        lock (_gate)
        {
            if (!_items.TryGetValue(key, out var item))
                _items[key] = item = _factory(key);
            return item;
        }
    }

    public T? Peek(string path)
    {
        lock (_gate)
            return _items.GetValueOrDefault(Normalize(path));
    }

    // Quita y libera (si es IDisposable) la instancia de esa ruta. Devuelve false si no existía.
    public bool Release(string path)
    {
        T? removed;
        lock (_gate)
        {
            if (!_items.Remove(Normalize(path), out removed))
                return false;
        }
        (removed as IDisposable)?.Dispose();
        return true;
    }

    public void Dispose()
    {
        List<T> all;
        lock (_gate)
        {
            all = [.. _items.Values];
            _items.Clear();
        }
        foreach (var item in all)
            (item as IDisposable)?.Dispose();
    }
}
