namespace Rama.Core.Jira;

public sealed record JiraBoardInfo(long Id, string Name, string? Type);

// Columna del board: nombre + ids de estados (en orden de configuración).
public sealed record JiraColumn(string Name, IReadOnlyList<string> StatusIds);

public sealed record JiraUser(string AccountId, string DisplayName, bool Active);

public sealed record JiraIssue(
    string Key,
    string Summary,
    string StatusId,
    string StatusName,
    string? AssigneeId,
    string? AssigneeName,
    string? IssueType,
    string? Priority,
    double? StoryPoints);

public sealed record FieldOption(string Id, string Label);

public sealed record TransitionField(
    string Key,
    string Name,
    bool Required,
    bool HasDefault,
    IReadOnlyList<FieldOption> AllowedValues);

public sealed record JiraTransition(
    string Id,
    string Name,
    string ToStatusId,
    string ToStatusName,
    IReadOnlyList<TransitionField> Fields);
