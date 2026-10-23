namespace Rama.Core.Jira;

public sealed record PersonLane(
    string? AccountId,
    string DisplayName,
    IReadOnlyList<IReadOnlyList<JiraIssue>> Cells,
    int Total);

// Modelo inmutable del board: issues agrupados por persona (filas) y columna (por id de estado).
public sealed class BoardModel
{
    public const string UnmappedColumnName = "Sin columna";
    public const string UnassignedName = "Sin asignar";

    private readonly IReadOnlyList<JiraColumn> _configured;

    public IReadOnlyList<JiraColumn> Columns { get; }
    public IReadOnlyList<JiraIssue> Issues { get; }
    public IReadOnlyList<PersonLane> Lanes { get; }
    public IReadOnlyList<int> ColumnTotals { get; }

    public BoardModel(IReadOnlyList<JiraColumn> columns, IEnumerable<JiraIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(columns);
        _configured = columns;
        Issues = issues.ToList();

        var byStatus = new Dictionary<string, int>();
        for (var i = 0; i < columns.Count; i++)
            foreach (var id in columns[i].StatusIds) byStatus.TryAdd(id, i);

        var unmapped = Issues.Any(x => !byStatus.ContainsKey(x.StatusId));
        Columns = unmapped ? [.. columns, new JiraColumn(UnmappedColumnName, [])] : columns;
        var unmappedIndex = columns.Count;

        Lanes = BuildLanes(x => byStatus.TryGetValue(x.StatusId, out var i) ? i : unmappedIndex);
        ColumnTotals = Enumerable.Range(0, Columns.Count).Select(c => Lanes.Sum(l => l.Cells[c].Count)).ToList();
    }

    private List<PersonLane> BuildLanes(Func<JiraIssue, int> indexOf)
    {
        var groups = Issues
            .GroupBy(x => x.AssigneeId)
            .Select(g => (Id: g.Key, Name: g.Key is null ? UnassignedName : g.First().AssigneeName ?? g.Key, Items: g.ToList()))
            .OrderBy(g => g.Id is null)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase);

        var result = new List<PersonLane>();
        foreach (var (id, name, items) in groups)
        {
            var cells = Enumerable.Range(0, Columns.Count)
                .Select(c => (IReadOnlyList<JiraIssue>)items.Where(x => indexOf(x) == c).ToList())
                .ToList();
            result.Add(new PersonLane(id, name, cells, items.Count));
        }
        return result;
    }

    public JiraIssue? Find(string key) => Issues.FirstOrDefault(x => x.Key == key);

    public JiraColumn? ColumnOf(JiraIssue issue) =>
        Columns.FirstOrDefault(c => c.StatusIds.Contains(issue.StatusId))
        ?? Columns.FirstOrDefault(c => c.Name == UnmappedColumnName);

    // Devuelve un modelo nuevo con el issue reemplazado (misma clave).
    public BoardModel WithIssue(JiraIssue updated) =>
        new(_configured, Issues.Select(x => x.Key == updated.Key ? updated : x));
}
