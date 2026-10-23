using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rama.Core.Jira;

// DTOs de red (forma documentada de Jira Cloud REST v3 / Agile 1.0).
internal sealed class BoardListDto
{
    public List<BoardDto>? Values { get; set; }
}

internal sealed class BoardDto
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
}

internal sealed class BoardConfigDto
{
    public ColumnConfigDto? ColumnConfig { get; set; }
}

internal sealed class ColumnConfigDto
{
    public List<ColumnDto>? Columns { get; set; }
}

internal sealed class ColumnDto
{
    public string? Name { get; set; }
    public List<ColumnStatusDto>? Statuses { get; set; }
}

internal sealed class ColumnStatusDto
{
    public string? Id { get; set; }
}

internal sealed class SearchRequestDto
{
    public string Jql { get; set; } = "";
    public List<string> Fields { get; set; } = [];
    public string? NextPageToken { get; set; }
    public int MaxResults { get; set; }
}

internal sealed class SearchResponseDto
{
    public List<IssueDto>? Issues { get; set; }
    public string? NextPageToken { get; set; }
}

internal sealed class IssueDto
{
    public string? Key { get; set; }
    public IssueFieldsDto? Fields { get; set; }
}

internal sealed class IssueFieldsDto
{
    public string? Summary { get; set; }
    public NamedDto? Status { get; set; }
    public UserDto? Assignee { get; set; }
    public NamedDto? Issuetype { get; set; }
    public NamedDto? Priority { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

internal sealed class NamedDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class UserDto
{
    public string? AccountId { get; set; }
    public string? DisplayName { get; set; }
    public bool Active { get; set; }
}

internal sealed class TransitionsResponseDto
{
    public List<TransitionDto>? Transitions { get; set; }
}

internal sealed class TransitionDto
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public NamedDto? To { get; set; }
    public Dictionary<string, TransitionFieldDto>? Fields { get; set; }
}

internal sealed class TransitionFieldDto
{
    public bool Required { get; set; }
    public string? Name { get; set; }
    public bool HasDefaultValue { get; set; }
    public JsonElement? DefaultValue { get; set; }
    public List<JsonElement>? AllowedValues { get; set; }
}

internal sealed class TransitionRequestDto
{
    public TransitionIdDto Transition { get; set; } = new();
    public IReadOnlyDictionary<string, JsonElement>? Fields { get; set; }
}

internal sealed class TransitionIdDto
{
    public string Id { get; set; } = "";
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BoardListDto))]
[JsonSerializable(typeof(BoardConfigDto))]
[JsonSerializable(typeof(SearchRequestDto))]
[JsonSerializable(typeof(SearchResponseDto))]
[JsonSerializable(typeof(UserDto))]
[JsonSerializable(typeof(List<UserDto>))]
[JsonSerializable(typeof(TransitionsResponseDto))]
[JsonSerializable(typeof(TransitionRequestDto))]
[ExcludeFromCodeCoverage(Justification = "Código source-gen")]
internal sealed partial class JiraJsonContext : JsonSerializerContext
{
}
