namespace Rama.Core.Jira;

// Superficie de Jira que usa el tablero (JiraClient la implementa; las pruebas usan un falso).
public interface IJiraBoardApi : IJiraTransitionApi
{
    Task<JiraUser> GetMyselfAsync(CancellationToken ct = default);

    // Columnas + campo de estimación del board (por defecto sin campo, para fakes simples).
    async Task<JiraBoardConfig> GetBoardConfigurationAsync(long boardId, CancellationToken ct = default) =>
        new(await GetBoardColumnsAsync(boardId, ct).ConfigureAwait(false), null);

    Task<IReadOnlyList<JiraBoardInfo>> GetBoardsAsync(string projectKeyOrId, CancellationToken ct = default);

    Task<IReadOnlyList<JiraColumn>> GetBoardColumnsAsync(long boardId, CancellationToken ct = default);

    Task<IReadOnlyList<JiraIssue>> SearchIssuesAsync(string jql, string? storyPointsField = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<JiraUser>> GetAssignableUsersAsync(string project, CancellationToken ct = default);
}
