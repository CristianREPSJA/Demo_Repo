namespace Rama.Core.Azure;

// Une "PRs donde soy revisor sin votar" + "aprobaciones de pipeline pendientes para mí".
// Los ids son estables entre sondeos (alimentan ItemDeduper); el id de PR incluye el último commit
// de origen: un push nuevo que reinicia los votos vuelve a notificar.
public sealed class PendingWorkService(AzureDevOpsClient client)
{
    private AzureUser? _me;

    public async Task<IReadOnlyList<PendingItem>> GetPendingAsync(IReadOnlySet<string>? memberOfIds = null,
        CancellationToken ct = default)
    {
        var me = _me ??= await client.GetCurrentUserAsync(ct).ConfigureAwait(false);
        var prsTask = client.ListPullRequestsAsync(reviewerId: me.Id, ct: ct);
        var approvalsTask = client.ListApprovalsForUserAsync(me.Id, memberOfIds, ct);
        await Task.WhenAll(prsTask, approvalsTask).ConfigureAwait(false);

        var items = new List<PendingItem>();
        items.AddRange(prsTask.Result.Where(p => NeedsMyVote(p, me.Id)).Select(ToItem));
        items.AddRange(approvalsTask.Result.Select(ToItem));
        return items;
    }

    // Pendiente: no soy el autor, no es borrador y mi voto directo es "sin votar".
    // Limitación: revisores que son grupos solo cuentan si el usuario figura con su propio id.
    public static bool NeedsMyVote(PullRequestInfo pr, string myId) =>
        !pr.IsDraft && !string.Equals(pr.CreatedById, myId, StringComparison.OrdinalIgnoreCase)
        && pr.Reviewers.Any(r => string.Equals(r.Id, myId, StringComparison.OrdinalIgnoreCase) && r.Vote == Vote.NoVote);

    public string PrKey(PullRequestInfo p) =>
        $"pr:{client.Connection.Organization}/{client.Connection.Project}/{p.RepositoryId}/{p.Id}@{p.SourceCommitId}";

    public string ApprovalKey(PipelineApproval a) =>
        $"approval:{client.Connection.Organization}/{client.Connection.Project}/{a.Id}";

    private PendingItem ToItem(PullRequestInfo p) => new(
        PrKey(p),
        PendingKind.PullRequestReview, $"#{p.Id} {p.Title}", $"{p.RepositoryName} · {p.CreatedByName}", p.WebUrl, p.CreatedOn);

    private PendingItem ToItem(PipelineApproval a) => new(
        ApprovalKey(a),
        PendingKind.PipelineApproval, a.PipelineName, a.Instructions ?? "Aprobación pendiente", null, a.CreatedOn);
}
