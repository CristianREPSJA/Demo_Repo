namespace Rama.Core.Terminal;

internal static class EnvLookup
{
    // Búsqueda sin distinguir mayúsculas (Windows usa "Path").
    public static string? Get(IReadOnlyDictionary<string, string> env, string key)
    {
        if (env.TryGetValue(key, out var value))
            return value;
        foreach (var (k, v) in env)
        {
            if (k.Equals(key, StringComparison.OrdinalIgnoreCase))
                return v;
        }
        return null;
    }
}
