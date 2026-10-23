using System.Globalization;

namespace Rama.Core.Clone;

// Lógica pura de la tabla: filtros y formatos en español de Chile.
public static class CloneListLogic
{
    public const string Unknown = "—";
    private const double BytesPerMb = 1024d * 1024d;
    private static readonly string[] Months = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

    public static IReadOnlyList<CloneRepoItem> Filter(IEnumerable<CloneRepoItem> items, string? search, string? project)
    {
        var term = (search ?? "").Trim();
        var proj = (project ?? "").Trim();
        return items.Where(i => proj.Length == 0 || string.Equals(i.Project, proj, StringComparison.OrdinalIgnoreCase))
            .Where(i => term.Length == 0 || Matches(i, term))
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<string> Projects(IEnumerable<CloneRepoItem> items) =>
        items.Select(i => i.Project).Where(p => p.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

    private static bool Matches(CloneRepoItem i, string term) =>
        i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (i.Description?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
        || i.Project.Contains(term, StringComparison.OrdinalIgnoreCase);

    // MB con un decimal y coma decimal; "—" si se desconoce.
    public static string FormatSize(long? bytes)
    {
        if (bytes is not > 0) return Unknown;
        var text = (bytes.Value / BytesPerMb).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');
        return $"{text} MB";
    }

    public static string FormatUpdated(DateTimeOffset? updated, DateTimeOffset now)
    {
        if (updated is not { } u) return Unknown;
        var local = u.ToOffset(now.Offset);
        var days = DateOnly.FromDateTime(now.DateTime).DayNumber - DateOnly.FromDateTime(local.DateTime).DayNumber;
        if (days == 0) return $"Hoy {local:HH:mm}";
        if (days == 1) return "Ayer";
        var text = $"{local.Day} {Months[local.Month - 1]}";
        return local.Year == now.Year ? text : $"{text} {local.Year}";
    }
}
