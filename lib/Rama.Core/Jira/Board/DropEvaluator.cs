using System.Text.Json;

namespace Rama.Core.Jira;

public enum DropKind { Valid, RequiresFields, Invalid, Current }

public sealed record ColumnDrop(DropKind Kind, string? Reason = null)
{
    public bool CanDrop => Kind is DropKind.Valid or DropKind.RequiresFields;
}

public enum FieldInputKind { Select, Text, Unsupported }

public static class DropEvaluator
{
    // Un resultado por columna, en el mismo orden.
    public static IReadOnlyList<ColumnDrop> Evaluate(JiraIssue issue, IReadOnlyList<JiraColumn> columns,
        IReadOnlyList<JiraTransition> transitions) =>
        columns.Select(c => EvaluateOne(issue, c, transitions)).ToList();

    private static ColumnDrop EvaluateOne(JiraIssue issue, JiraColumn target, IReadOnlyList<JiraTransition> transitions)
    {
        if (target.StatusIds.Contains(issue.StatusId)) return new ColumnDrop(DropKind.Current);
        if (target.StatusIds.Count == 0)
            return new ColumnDrop(DropKind.Invalid, $"La columna {target.Name} no tiene estados");
        var plan = TransitionPlanner.Plan(issue, target, transitions);
        return plan.Kind switch
        {
            PlanKind.Allowed => new ColumnDrop(DropKind.Valid),
            PlanKind.RequiresFields => new ColumnDrop(DropKind.RequiresFields),
            _ => new ColumnDrop(DropKind.Invalid, plan.Reason),
        };
    }
}

// Convierte lo escrito en el diálogo de campos requeridos al JSON que espera la API de transiciones.
public static class FieldValueBuilder
{
    public static FieldInputKind KindOf(TransitionField f)
    {
        if (f.AllowedValues.Count > 0) return FieldInputKind.Select;
        return f.SchemaType is null or "string" ? FieldInputKind.Text : FieldInputKind.Unsupported;
    }

    // null si algún campo no se puede construir (falta valor o tipo no soportado).
    public static IReadOnlyDictionary<string, JsonElement>? Build(
        IEnumerable<(TransitionField Field, string? Value)> inputs)
    {
        var result = new Dictionary<string, JsonElement>();
        foreach (var (field, value) in inputs)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var kind = KindOf(field);
            if (kind == FieldInputKind.Unsupported) return null;
            result[field.Key] = kind == FieldInputKind.Select
                ? SelectJson(value, field.SchemaType == "array")
                : TextJson(value.Trim());
        }
        return result;
    }

    private static JsonElement TextJson(string text) => Write(w => w.WriteStringValue(text));

    private static JsonElement SelectJson(string id, bool array) => Write(w =>
    {
        if (array) w.WriteStartArray();
        w.WriteStartObject();
        w.WriteString("id", id);
        w.WriteEndObject();
        if (array) w.WriteEndArray();
    });

    private static JsonElement Write(Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) body(w);
        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }
}
