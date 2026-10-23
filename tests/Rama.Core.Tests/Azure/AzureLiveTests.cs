using Rama.Core.Azure;
using Rama.Core.Http;

namespace Rama.Core.Tests.Azure;

// Se omite salvo que existan RAMA_AZDO_ORG, RAMA_AZDO_PROJECT y RAMA_AZDO_PAT_FILE.
// Usar solo contra un proyecto de pruebas: vota en un PR activo y aprueba etapas pendientes.
public sealed class LiveAzureFactAttribute : FactAttribute
{
    public LiveAzureFactAttribute()
    {
        if (AzureLiveTests.Env("RAMA_AZDO_ORG") is null || AzureLiveTests.Env("RAMA_AZDO_PROJECT") is null
            || AzureLiveTests.Env("RAMA_AZDO_PAT_FILE") is not { } f || !File.Exists(f))
            Skip = "Prueba contra Azure DevOps real: faltan RAMA_AZDO_ORG/PROJECT/PAT_FILE.";
    }
}

[Trait("Category", "Live")]
public class AzureLiveTests
{
    internal static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    private static AzureDevOpsClient MakeClient()
    {
        var pat = File.ReadAllText(Env("RAMA_AZDO_PAT_FILE")!).Trim();
        return new AzureDevOpsClient(new HttpClient(), new AzureConnection(Env("RAMA_AZDO_ORG")!, Env("RAMA_AZDO_PROJECT")!),
            pat, new ApiClientOptions { MaxRetries = 1 });
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

        await c.UpdateApprovalAsync(ap.Id, approve: true, comment: "Aprobado por prueba en vivo de Rama");
        Assert.DoesNotContain(await c.ListPendingApprovalsAsync(), a => a.Id == ap.Id);
    }
}
