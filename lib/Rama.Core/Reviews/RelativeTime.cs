namespace Rama.Core.Reviews;

public static class RelativeTime
{
    // "hace 25 min"; con includeSeconds, "hace 12 s" para intervalos menores a un minuto.
    public static string Ago(DateTimeOffset when, DateTimeOffset now, bool includeSeconds = false)
    {
        var d = now - when;
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d < TimeSpan.FromMinutes(1))
            return includeSeconds ? $"hace {(int)d.TotalSeconds} s" : "hace un momento";
        if (d < TimeSpan.FromHours(1)) return $"hace {(int)d.TotalMinutes} min";
        if (d < TimeSpan.FromDays(1)) return $"hace {(int)d.TotalHours} h";
        return $"hace {(int)d.TotalDays} d";
    }
}
