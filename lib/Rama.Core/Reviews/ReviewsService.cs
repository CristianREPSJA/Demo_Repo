using Rama.Core.Azure;
using Rama.Core.Http;

namespace Rama.Core.Reviews;

// Orquesta las llamadas a Azure DevOps de la pantalla "Revisiones pendientes".
public sealed class ReviewsService(AzureDevOpsClient client, PendingWorkService pending, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    public async Task<ReviewsSnapshot> LoadAsync(CancellationToken ct = default)
    {
        var me = await pending.GetMeAsync(ct).ConfigureAwait(false);
        var reviewing = client.ListPullRequestsAsync(reviewerId: me.Id, ct: ct);
        var mine = client.ListPullRequestsAsync(creatorId: me.Id, ct: ct);
        var approvals = client.ListApprovalsForUserAsync(me.Id, null, ct);
        await Task.WhenAll(reviewing, mine, approvals).ConfigureAwait(false);

        var prs = PendingWorkService.NeedingMyVote(await reviewing.ConfigureAwait(false), me.Id)
            .Select(p => ReviewMapper.ToPendingPr(p, pending.PrKey(p))).ToList();
        var deployments = (await approvals.ConfigureAwait(false)).Select(a => ReviewMapper.ToDeployment(a, pending.ApprovalKey(a))).ToList();
        var myPrs = (await mine.ConfigureAwait(false)).Where(p => !p.IsDraft)
            .Select(p => ReviewMapper.ToMyPr(p, "mine:" + pending.PrKey(p))).ToList();
        return new ReviewsSnapshot(me, prs, deployments, myPrs, _time.GetUtcNow());
    }

    public async Task<PullRequestDetail> LoadDetailAsync(PullRequestInfo listed, string myId, CancellationToken ct = default)
    {
        var prTask = client.GetPullRequestAsync(listed.RepositoryId, listed.Id, ct);
        var commitsTask = client.GetCommitCountAsync(listed.RepositoryId, listed.Id, ct);
        var policyTask = LoadPoliciesAsync(listed, ct);
        await Task.WhenAll(prTask, commitsTask, policyTask).ConfigureAwait(false);

        var pr = await prTask.ConfigureAwait(false);
        var summary = await policyTask.ConfigureAwait(false);
        var commits = await commitsTask.ConfigureAwait(false);
        return new PullRequestDetail(pr, commits, ReviewMapper.CommitsText(commits),
            ReviewMapper.BranchName(pr.SourceBranch), ReviewMapper.BranchName(pr.TargetBranch),
            summary.Items.Select(ReviewMapper.ToPolicyRow).ToList(),
            pr.Reviewers.Select(r => ReviewMapper.ToReviewerRow(r, myId)).ToList(),
            ReviewMapper.CompleteBlockReason(pr, summary));
    }

    // Las políticas son preview: un fallo que no sea de permisos no debe ocultar el resto del detalle.
    private async Task<PolicySummary> LoadPoliciesAsync(PullRequestInfo pr, CancellationToken ct)
    {
        if (pr.ProjectId is null) return PolicySummary.From([]);
        try { return await client.GetPolicySummaryAsync(pr.ProjectId, pr.Id, ct).ConfigureAwait(false); }
        catch (ApiException ex) when (ex is not ApiAuthException) { return PolicySummary.From([]); }
    }

    public Task VoteAsync(PullRequestInfo pr, string myId, Vote vote, CancellationToken ct = default) =>
        client.VoteAsync(pr.RepositoryId, pr.Id, myId, vote, ct);

    public Task<IReadOnlyList<RunStage>> LoadRunStagesAsync(int runId, CancellationToken ct = default) =>
        client.GetRunStagesAsync(runId, ct);

    public Task DecideDeploymentAsync(PipelineApproval a, bool approve, string? comment, CancellationToken ct = default) =>
        client.UpdateApprovalAsync(a.Id, approve, string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(), ct);
}
