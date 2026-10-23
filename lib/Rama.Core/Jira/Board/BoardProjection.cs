using System.Globalization;

namespace Rama.Core.Jira;

public enum IssueKind { Story, Bug, Task }

public sealed record PersonChip(string Id, string Name, string Initials, int Count, bool IsMe, int PaletteIndex);

public sealed record CardInfo(
    JiraIssue Issue,
    IssueKind Kind,
    string TypeLabel,
    string? Priority,
    bool IsHighPriority,
    string? PointsLabel,
    string? Branch)
{
    public string Key => Issue.Key;
    public string Summary => Issue.Summary;
}

public sealed record ColumnInfo(int Index, JiraColumn Column, IReadOnlyList<CardInfo> Cards);

public sealed record BoardView(IReadOnlyList<PersonChip> People, string SelectedId, IReadOnlyList<ColumnInfo> Columns);

// Proyección pura del modelo a lo que dibuja el tablero: personas con conteos, columnas y tarjetas filtradas.
public static class BoardProjection
{
    public const string UnassignedId = "~unassigned";
    public const int PaletteSize = 5;

    private static readonly HashSet<string> HighPriorities = new(StringComparer.OrdinalIgnoreCase)
        { "Alta", "Más alta", "Mas alta", "Crítica", "Critica", "High", "Highest", "Critical", "Blocker" };

    public static BoardView Build(BoardModel model, IEnumerable<JiraUser> assignable, string? myId,
        string? selectedId, string? query, Func<string, string?>? branchOf = null)
    {
        var people = BuildPeople(model, assignable, myId);
        var selected = people.Any(p => p.Id == selectedId) ? selectedId!
            : people.FirstOrDefault(p => p.IsMe)?.Id ?? people.FirstOrDefault()?.Id ?? "";

        var q = (query ?? "").Trim();
        var mine = model.Issues.Where(i => (i.AssigneeId ?? UnassignedId) == selected && Matches(i, q)).ToList();
        var columns = model.Columns
            .Select((c, idx) => new ColumnInfo(idx, c, mine
                .Where(i => model.ColumnOf(i) == c)
                .Select(i => ToCard(i, branchOf?.Invoke(i.Key))).ToList()))
            .ToList();
        return new BoardView(people, selected, columns);
    }

    private static List<PersonChip> BuildPeople(BoardModel model, IEnumerable<JiraUser> assignable, string? myId)
    {
        var counts = model.Issues.GroupBy(i => i.AssigneeId ?? UnassignedId).ToDictionary(g => g.Key, g => g.Count());
        var names = new Dictionary<string, string>();
        foreach (var u in assignable) names.TryAdd(u.AccountId, u.DisplayName);
        foreach (var i in model.Issues.Where(i => i.AssigneeId is not null))
            names.TryAdd(i.AssigneeId!, i.AssigneeName ?? i.AssigneeId!);

        var chips = names
            .Where(kv => counts.ContainsKey(kv.Key) || kv.Key == myId)
            .Select(kv => new PersonChip(kv.Key, kv.Value, Initials(kv.Value),
                counts.GetValueOrDefault(kv.Key), kv.Key == myId, PaletteOf(kv.Key)))
            .OrderBy(p => !p.IsMe).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (counts.TryGetValue(UnassignedId, out var n))
            chips.Add(new PersonChip(UnassignedId, BoardModel.UnassignedName, "?", n, false, PaletteSize - 1));
        return chips;
    }

    private static bool Matches(JiraIssue i, string q) =>
        q.Length == 0
        || i.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
        || i.Summary.Contains(q, StringComparison.OrdinalIgnoreCase);

    public static CardInfo ToCard(JiraIssue i, string? branch)
    {
        var priority = string.IsNullOrWhiteSpace(i.Priority) ? null : i.Priority;
        var points = i.StoryPoints is { } p ? $"{p.ToString("0.##", CultureInfo.InvariantCulture)} pts" : null;
        return new CardInfo(i, KindOf(i.IssueType), i.IssueType ?? "", priority,
            priority is not null && HighPriorities.Contains(priority), points, branch);
    }

    public static IssueKind KindOf(string? type)
    {
        var t = (type ?? "").ToLowerInvariant();
        if (t.Contains("histor") || t.Contains("story") || t.Contains("épica") || t.Contains("epic")) return IssueKind.Story;
        if (t.Contains("error") || t.Contains("bug") || t.Contains("defect")) return IssueKind.Bug;
        return IssueKind.Task;
    }

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    // Estable entre ejecuciones (no usa string.GetHashCode).
    public static int PaletteOf(string id) => id.Aggregate(0, (acc, c) => (acc + c) % 997) % PaletteSize;
}

public static class BoardText
{
    // 40 -> "40 s", 150 -> "2 min", 7300 -> "2 h".
    public static string SyncAge(TimeSpan age)
    {
        var s = Math.Max(0, (int)age.TotalSeconds);
        return s < 60 ? $"{s} s" : s < 3600 ? $"{s / 60} min" : $"{s / 3600} h";
    }

    // Primera rama (en el orden dado) cuyo nombre contiene la clave del ticket.
    public static string? FindBranch(string key, IEnumerable<string> branchNames) =>
        branchNames.FirstOrDefault(b => BranchTicketParser.ExtractKey(b) == key);
}
