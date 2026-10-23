using System.Text;

namespace Rama.Core.Jira;

// Construye JQL con escape de literales.
public static class JqlBuilder
{
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }

    // Sin includeMe ni ids no se agrega cláusula assignee.
    public static string Build(string project, bool includeMe = false,
        IEnumerable<string>? accountIds = null, bool openSprintsOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        var clauses = new List<string> { $"project = {Quote(project)}" };

        var people = new List<string>();
        if (includeMe) people.Add("assignee = currentUser()");
        foreach (var id in accountIds ?? [])
            people.Add($"assignee = {Quote(id)}");
        if (people.Count == 1) clauses.Add(people[0]);
        else if (people.Count > 1) clauses.Add($"({string.Join(" OR ", people)})");

        if (openSprintsOnly) clauses.Add("sprint in openSprints()");
        return string.Join(" AND ", clauses) + " ORDER BY Rank ASC";
    }
}
