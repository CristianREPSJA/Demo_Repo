using System.Text.Json;
using System.Text.RegularExpressions;
using Rama.Core.Http;

namespace Rama.Core.Jira;

public interface IJiraTransitionApi
{
    Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string issueKey, CancellationToken ct = default);

    Task TransitionAsync(string issueKey, string transitionId,
        IReadOnlyDictionary<string, JsonElement>? fields = null, CancellationToken ct = default);
}

// Cliente Jira Cloud: Basic email:API token.
public sealed partial class JiraClient : ApiClientBase, IJiraBoardApi
{
    public const int PageSize = 100;
    public const int MaxPages = 20;
    private const string TokenHint = "Revisa el correo y el API token de Jira.";

    private readonly string _baseUrl;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex SitePattern();

    [GeneratedRegex("^[A-Za-z0-9-]+$")]
    private static partial Regex CloudIdPattern();

    // baseUrl debe ser https, sin barra final.
    public JiraClient(HttpClient http, string baseUrl, string email, string apiToken, ApiClientOptions? options = null)
        : base(http, ApiAuth.Basic(RequireEmail(email), apiToken),
            (options ?? new ApiClientOptions()) with { MissingScopeHint = TokenHint })
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("La URL de Jira debe ser https.", nameof(baseUrl));
        _baseUrl = baseUrl.TrimEnd('/');
    }

    private static string RequireEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email;
    }

    public static string SiteBaseUrl(string site)
    {
        site = (site ?? "").Trim().ToLowerInvariant();
        if (site.EndsWith(".atlassian.net")) site = site[..^".atlassian.net".Length];
        if (!SitePattern().IsMatch(site)) throw new ArgumentException("Sitio de Jira inválido.", nameof(site));
        return $"https://{site}.atlassian.net";
    }

    // Verificado 2026-10-23: tokens con alcance (scoped) solo funcionan vía api.atlassian.com/ex/jira/{cloudId}; en el sitio dan 401.
    public static string ScopedBaseUrl(string cloudId)
    {
        if (string.IsNullOrWhiteSpace(cloudId) || !CloudIdPattern().IsMatch(cloudId))
            throw new ArgumentException("cloudId inválido.", nameof(cloudId));
        return $"https://api.atlassian.com/ex/jira/{cloudId}";
    }

    // cloudId del sitio sin autenticación: GET {site}/_edge/tenant_info (verificado 2026-10-23).
    public static async Task<string> ResolveCloudIdAsync(HttpClient http, string site, CancellationToken ct = default)
    {
        var url = $"{SiteBaseUrl(site)}/_edge/tenant_info";
        try
        {
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new ApiException(resp.StatusCode, "No se pudo obtener el identificador del sitio de Jira.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var id = doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("cloudId", out var el) && el.ValueKind == JsonValueKind.String
                ? el.GetString() : null;
            if (string.IsNullOrEmpty(id) || !CloudIdPattern().IsMatch(id))
                throw new ApiException(0, "El sitio de Jira devolvió un identificador inválido.");
            return id;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            throw new ApiException(0, "No se pudo conectar con Jira para detectar el sitio.");
        }
    }

    // Prueba de conexión: GET /rest/api/3/myself.
    public async Task<JiraUser> GetMyselfAsync(CancellationToken ct = default)
    {
        var dto = await GetJsonAsync($"{_baseUrl}/rest/api/3/myself", JiraJsonContext.Default.UserDto, ct)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(dto?.AccountId))
            throw new ApiException(0, "/rest/api/3/myself no devolvió el usuario autenticado.");
        return new JiraUser(dto.AccountId, dto.DisplayName ?? dto.AccountId, dto.Active);
    }

    public async Task<IReadOnlyList<JiraBoardInfo>> GetBoardsAsync(string projectKeyOrId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/agile/1.0/board?projectKeyOrId={Uri.EscapeDataString(projectKeyOrId)}";
        var dto = await GetJsonAsync(url, JiraJsonContext.Default.BoardListDto, ct).ConfigureAwait(false);
        return (dto?.Values ?? [])
            .Select(b => new JiraBoardInfo(b.Id, b.Name ?? $"Board {b.Id}", b.Type)).ToList();
    }

    public async Task<IReadOnlyList<JiraColumn>> GetBoardColumnsAsync(long boardId, CancellationToken ct = default) =>
        (await GetBoardConfigurationAsync(boardId, ct).ConfigureAwait(false)).Columns;

    public async Task<JiraBoardConfig> GetBoardConfigurationAsync(long boardId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/agile/1.0/board/{boardId}/configuration";
        var dto = await GetJsonAsync(url, JiraJsonContext.Default.BoardConfigDto, ct).ConfigureAwait(false);
        var columns = (dto?.ColumnConfig?.Columns ?? [])
            .Select(c => new JiraColumn(c.Name ?? "",
                (c.Statuses ?? []).Where(s => !string.IsNullOrEmpty(s.Id)).Select(s => s.Id!).ToList()))
            .ToList();
        // Verificado 2026-10-23: estimation = {"type":"field","field":{"fieldId":"customfield_10024"}}.
        var est = dto?.Estimation;
        var field = string.Equals(est?.Type, "field", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(est?.Field?.FieldId) ? est.Field.FieldId : null;
        return new JiraBoardConfig(columns, field);
    }

    public async Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, string? storyPointsField = null,
        CancellationToken ct = default)
    {
        var fields = new List<string> { "summary", "status", "assignee", "issuetype", "priority" };
        if (!string.IsNullOrWhiteSpace(storyPointsField)) fields.Add(storyPointsField);

        var result = new List<JiraIssue>();
        string? token = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var body = new SearchRequestDto { Jql = jql, Fields = fields, NextPageToken = token, MaxResults = PageSize };
            var resp = await SendJsonAsync(HttpMethod.Post, $"{_baseUrl}/rest/api/3/search/jql", body,
                JiraJsonContext.Default.SearchRequestDto, JiraJsonContext.Default.SearchResponseDto, ct)
                .ConfigureAwait(false);
            result.AddRange((resp?.Issues ?? []).Select(i => MapIssue(i, storyPointsField)));
            token = resp?.NextPageToken;
            if (string.IsNullOrEmpty(token)) break;
        }
        return result;
    }

    public async Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(string project, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/api/3/user/assignable/search?project={Uri.EscapeDataString(project)}&maxResults=100";
        var users = await GetJsonAsync(url, JiraJsonContext.Default.ListUserDto, ct).ConfigureAwait(false);
        return (users ?? [])
            .Where(u => !string.IsNullOrEmpty(u.AccountId))
            .Select(u => new JiraUser(u.AccountId!, u.DisplayName ?? u.AccountId!, u.Active)).ToList();
    }

    public async Task<IReadOnlyList<JiraTransition>> GetTransitionsAsync(string issueKey, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions?expand=transitions.fields";
        var dto = await GetJsonAsync(url, JiraJsonContext.Default.TransitionsResponseDto, ct).ConfigureAwait(false);
        return (dto?.Transitions ?? [])
            .Where(t => t.Id is not null && t.To?.Id is not null).Select(MapTransition).ToList();
    }

    public async Task TransitionAsync(string issueKey, string transitionId,
        IReadOnlyDictionary<string, JsonElement>? fields = null, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/api/3/issue/{Uri.EscapeDataString(issueKey)}/transitions";
        var body = new TransitionRequestDto
        {
            Transition = new TransitionIdDto { Id = transitionId },
            Fields = fields is { Count: > 0 } ? fields : null,
        };
        try
        {
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = System.Net.Http.Json.JsonContent.Create(body, JiraJsonContext.Default.TransitionRequestDto),
            }, ct).ConfigureAwait(false);
        }
        catch (ApiAuthException ex) when (ex.Reason == ScopeMismatchReason)
        {
            throw new ApiAuthException(ex.StatusCode, WriteScopeHint, ex.Reason);
        }
    }

    private const string WriteScopeHint = "Para mover tarjetas, el token de Jira necesita el permiso write:jira-work.";

    private static JiraIssue MapIssue(IssueDto i, string? spField)
    {
        var f = i.Fields;
        double? points = null;
        if (spField is not null && f?.Extra is { } extra && extra.TryGetValue(spField, out var el)
            && el.ValueKind == JsonValueKind.Number)
            points = el.GetDouble();

        return new JiraIssue(i.Key ?? "", f?.Summary ?? "", f?.Status?.Id ?? "", f?.Status?.Name ?? "",
            f?.Assignee?.AccountId, f?.Assignee?.DisplayName, f?.Issuetype?.Name, f?.Priority?.Name, points);
    }

    private static JiraTransition MapTransition(TransitionDto t) =>
        new(t.Id!, t.Name ?? t.Id!, t.To!.Id!, t.To.Name ?? t.To.Id!,
            (t.Fields ?? []).Select(kv => MapField(kv.Key, kv.Value)).ToList());

    private static TransitionField MapField(string key, TransitionFieldDto f)
    {
        var hasDefault = f.HasDefaultValue
            || f.DefaultValue is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) };
        var options = (f.AllowedValues ?? []).Select(MapOption).OfType<FieldOption>().ToList();
        return new TransitionField(key, f.Name ?? key, f.Required, hasDefault, options, f.Schema?.Type);
    }

    private static FieldOption? MapOption(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object) return null;
        var id = Prop(e, "id") ?? Prop(e, "key");
        var label = Prop(e, "name") ?? Prop(e, "value") ?? id;
        return id is null ? null : new FieldOption(id, label ?? id);
    }

    private static string? Prop(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
