using Rama.Core.Accounts;
using Rama.Core.Http;
using Rama.Core.Jira;

namespace Rama.Core.Tests.Jira;

// Se omite salvo que existan RAMA_JIRA_SITE, RAMA_JIRA_EMAIL, RAMA_JIRA_TOKEN_FILE (archivo con el token) y RAMA_JIRA_PROJECT.
// SOLO LECTURA: está prohibido llamar DoTransition / POST transitions o cualquier escritura en pruebas en vivo.
public sealed class LiveJiraFactAttribute : FactAttribute
{
    public LiveJiraFactAttribute()
    {
        if (JiraLiveTests.Env("RAMA_JIRA_SITE") is null || JiraLiveTests.Env("RAMA_JIRA_EMAIL") is null
            || JiraLiveTests.Env("RAMA_JIRA_PROJECT") is null
            || JiraLiveTests.Env("RAMA_JIRA_TOKEN_FILE") is not { } f || !File.Exists(f))
            Skip = "Prueba contra Jira real: faltan RAMA_JIRA_SITE/EMAIL/TOKEN_FILE/PROJECT.";
    }
}

[Trait("Category", "Live")]
public class JiraLiveTests
{
    internal static string? Env(string n) => Environment.GetEnvironmentVariable(n) is { Length: > 0 } v ? v : null;

    // Misma lógica que la app: sitio y, ante 401, gateway con cloudId.
    private static Task<JiraConnection> ConnectAsync()
    {
        var token = File.ReadAllText(Env("RAMA_JIRA_TOKEN_FILE")!).Trim();
        var account = new JiraAccount(Env("RAMA_JIRA_SITE")!, Env("RAMA_JIRA_EMAIL")!, Env("RAMA_JIRA_PROJECT")!);
        return JiraConnector.ConnectAsync(new HttpClient(), account, token, new ApiClientOptions { MaxRetries = 1 });
    }

    [LiveJiraFact]
    public async Task ReadOnlyFlow_WorksAgainstRealJira()
    {
        var conn = await ConnectAsync();
        Assert.NotEqual(JiraAuthMode.Auto, conn.Account.Mode);
        Assert.False(string.IsNullOrEmpty(conn.User.AccountId));
        var c = conn.Client;
        var project = conn.Account.ProjectKey;

        var boards = await c.GetBoardsAsync(project);
        Assert.NotEmpty(boards);
        var cfg = await c.GetBoardConfigurationAsync(boards[0].Id);
        Assert.NotEmpty(cfg.Columns);
        Assert.False(string.IsNullOrEmpty(cfg.EstimationField), "El board no expone un campo de estimación.");

        var issues = await c.SearchIssuesAsync(
            $"project = \"{project}\" AND assignee = currentUser() ORDER BY updated DESC", cfg.EstimationField);
        Assert.All(issues, i => Assert.False(string.IsNullOrEmpty(i.StatusId)));

        Assert.NotEmpty(await c.GetAssignableUsersAsync(project));

        if (issues.Count > 0)
            Assert.NotNull(await c.GetTransitionsAsync(issues[0].Key)); // solo GET; nunca TransitionAsync
    }

    // ÚNICA prueba que escribe: mueve UN issue a una columna, como arrastrar la tarjeta en la app.
    // Requiere opt-in explícito con RAMA_JIRA_MOVE_ISSUE, RAMA_JIRA_MOVE_BOARD y RAMA_JIRA_MOVE_COLUMN.
    [LiveJiraFact]
    public async Task MoveIssue_ToColumn_WhenExplicitlyAuthorized()
    {
        if (Env("RAMA_JIRA_MOVE_ISSUE") is not { } key || Env("RAMA_JIRA_MOVE_BOARD") is not { } boardRaw
            || Env("RAMA_JIRA_MOVE_COLUMN") is not { } columnName || !long.TryParse(boardRaw, out var boardId))
            return;

        var c = (await ConnectAsync()).Client;
        var column = Assert.Single((await c.GetBoardConfigurationAsync(boardId)).Columns,
            col => string.Equals(col.Name, columnName, StringComparison.OrdinalIgnoreCase));
        var issue = Assert.Single(await c.SearchIssuesAsync($"key = \"{key}\""));

        var plan = TransitionPlanner.Plan(issue, column, await c.GetTransitionsAsync(key));
        Assert.Equal(PlanKind.Allowed, plan.Kind);
        await c.TransitionAsync(key, plan.Transition!.Id);

        var after = Assert.Single(await c.SearchIssuesAsync($"key = \"{key}\""));
        Assert.Contains(after.StatusId, column.StatusIds);
    }
}
