using Rama.Core.Azure;
using Rama.Core.Http;

namespace Rama.Core.Tests.Azure;

// Se omite salvo que existan RAMA_AZDO_ORG, RAMA_AZDO_PROJECT y una credencial:
// RAMA_AZDO_BEARER_FILE (JSON con access_token de Entra; tiene prioridad) o RAMA_AZDO_PAT_FILE.
// Usar solo contra un proyecto de pruebas: vota en un PR activo y aprueba etapas pendientes.
public sealed class LiveAzureFactAttribute : FactAttribute
{
    public LiveAzureFactAttribute()
    {
        if (AzureLiveTests.Env("RAMA_AZDO_ORG") is null || AzureLiveTests.Env("RAMA_AZDO_PROJECT") is null
            || !(Exists("RAMA_AZDO_BEARER_FILE") || Exists("RAMA_AZDO_PAT_FILE")))
            Skip = "Prueba contra Azure DevOps real: faltan RAMA_AZDO_ORG/PROJECT y BEARER_FILE o PAT_FILE.";
    }

    private static bool Exists(string name) => AzureLiveTests.Env(name) is { } f && File.Exists(f);
}

// Token de Entra leído desde archivo (sin renovación).
internal sealed class FileTokenSource(string path) : IAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return Task.FromResult(doc.RootElement.GetProperty("access_token").GetString()!);
    }
}

[Trait("Category", "Live")]
public class AzureLiveTests
{
    internal static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static AzureConnection Connection => new(Env("RAMA_AZDO_ORG")!, Env("RAMA_AZDO_PROJECT")!);

    private static AzureDevOpsClient MakeClient()
    {
        var options = new ApiClientOptions { MaxRetries = 1 };
        if (Env("RAMA_AZDO_BEARER_FILE") is { } bearer && File.Exists(bearer))
            return new AzureDevOpsClient(new HttpClient(), Connection, new BearerAuthProvider(new FileTokenSource(bearer)), options);
        var pat = File.ReadAllText(Env("RAMA_AZDO_PAT_FILE")!).Trim();
        return new AzureDevOpsClient(new HttpClient(), Connection, pat, options);
    }

    // git real con el Bearer inyectado por GitAuthEnvironment (solo modo Entra).
    [LiveAzureFact]
    public async Task GitLsRemote_WithEntraBearer_ListsMain()
    {
        if (Env("RAMA_AZDO_BEARER_FILE") is not { } bearer || !File.Exists(bearer)) return;
        var token = await new FileTokenSource(bearer).GetAccessTokenAsync(false, default);
        var repo = (await MakeClient().ListReposAsync()).First();

        var psi = new System.Diagnostics.ProcessStartInfo("git", $"ls-remote {repo.RemoteUrl} refs/heads/main")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";
        Rama.Core.Azure.Entra.GitAuthEnvironment.Apply(psi.Environment, token);

        using var p = System.Diagnostics.Process.Start(psi)!;
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Assert.True(p.ExitCode == 0, "git ls-remote falló: " + stderr.Replace(token, "***"));
        Assert.Contains("refs/heads/main", stdout);
    }

    // Credencial inválida: Azure DevOps responde 203 + HTML de inicio de sesión; debe ser error de autenticación.
    [LiveAzureFact]
    public async Task InvalidPat_RaisesAuthError()
    {
        var c = new AzureDevOpsClient(new HttpClient(), Connection, "invalido", new ApiClientOptions { MaxRetries = 0 });
        await Assert.ThrowsAsync<ApiAuthException>(() => c.ListReposAsync());
    }

    [LiveAzureFact]
    public async Task ReviewFlow_WorksAgainstRealService()
    {
        var c = MakeClient();

        var me = await c.GetCurrentUserAsync();
        Assert.NotEqual(me.Id, me.DisplayName);

        var repos = await c.ListReposAsync();
        Assert.NotEmpty(repos);
        Assert.All(repos, r => Assert.NotNull(AzureDevOpsClient.TryValidateCloneUrl(r.RemoteUrl)));

        var prs = await c.ListPullRequestsAsync(reviewerId: me.Id);
        var pr = Assert.Single(prs.OrderBy(p => p.Id).Take(1));
        Assert.Contains(pr.Reviewers, r => r.Id == me.Id);

        var full = await c.GetPullRequestAsync(pr.RepositoryId, pr.Id);
        Assert.Equal(pr.Id, full.Id);
        Assert.True(await c.GetCommitCountAsync(pr.RepositoryId, pr.Id) >= 1);

        var policies = await c.GetPolicySummaryAsync(full.ProjectId!, pr.Id);
        Assert.All(policies.Items, i => Assert.NotEqual(PolicyStatus.Unknown, i.Status));

        await c.VoteAsync(pr.RepositoryId, pr.Id, me.Id, Vote.WaitingForAuthor);
        Assert.Equal(Vote.WaitingForAuthor, (await c.GetPullRequestAsync(pr.RepositoryId, pr.Id)).Reviewers.Single(r => r.Id == me.Id).Vote);
        await c.VoteAsync(pr.RepositoryId, pr.Id, me.Id, Vote.Approved);
        Assert.Equal(Vote.Approved, (await c.GetPullRequestAsync(pr.RepositoryId, pr.Id)).Reviewers.Single(r => r.Id == me.Id).Vote);
    }

    [LiveAzureFact]
    public async Task PendingApproval_CanBeApprovedByAssignedUser()
    {
        var c = MakeClient();
        var me = await c.GetCurrentUserAsync();

        var mine = await c.ListApprovalsForUserAsync(me.Id);
        if (Env("RAMA_AZDO_EXPECT_APPROVAL") is null && mine.Count == 0) return;
        var ap = Assert.Single(mine.Take(1));
        Assert.NotEmpty(ap.PipelineName);
        Assert.Contains(ap.Steps, s => s.ApproverId == me.Id && s.ApproverName.Length > 0);

        Assert.NotNull(ap.RunId);
        Assert.NotEmpty(await c.GetRunStagesAsync(ap.RunId.Value));

        await c.UpdateApprovalAsync(ap.Id, approve: true, comment: "Aprobado por prueba en vivo de Rama");
        Assert.DoesNotContain(await c.ListPendingApprovalsAsync(), a => a.Id == ap.Id);
    }
}
