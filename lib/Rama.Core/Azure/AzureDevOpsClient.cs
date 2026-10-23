using System.Net.Http.Json;
using Rama.Core.Http;

namespace Rama.Core.Azure;

// Cliente REST de Azure DevOps (api-version 7.1). Auth Basic ":PAT".
public sealed class AzureDevOpsClient : ApiClientBase
{
    public const string CodeScopeHint = "Falta el permiso del PAT: Code (Read & write).";
    public const string BuildScopeHint = "Falta el permiso del PAT: Build (Read & execute) y Pipeline Resources (Use).";

    private const string V = "api-version=" + AzureConnection.ApiVersion;
    private const string PolicyV = "api-version=7.1-preview.1";

    public AzureConnection Connection { get; }

    public AzureDevOpsClient(HttpClient http, AzureConnection connection, string pat, ApiClientOptions? options = null)
        : base(http, ApiAuth.Pat(pat), options)
    {
        Connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    private string Org => Connection.OrgUrl;
    private string Proj => Connection.ProjectUrl;

    // Reetiqueta 401/403 con el permiso del PAT que corresponde al recurso.
    private static async Task<T> WithHint<T>(string hint, Func<Task<T>> call)
    {
        try { return await call().ConfigureAwait(false); }
        catch (ApiAuthException ex) { throw new ApiAuthException(ex.StatusCode, hint); }
    }

    public Task<AzureUser> GetCurrentUserAsync(CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var dto = await GetJsonAsync($"{Org}/_apis/connectionData?{V}-preview", AzureJsonContext.Default.ConnectionDataDto, ct)
                .ConfigureAwait(false);
            var u = dto?.AuthenticatedUser;
            if (u?.Id is null) throw new ApiException(0, "connectionData no devolvió el usuario autenticado.");
            return new AzureUser(u.Id, u.DisplayName ?? u.Id);
        });

    public Task<IReadOnlyList<AzureRepo>> ListReposAsync(CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var dto = await GetJsonAsync($"{Proj}/_apis/git/repositories?{V}", AzureJsonContext.Default.ListDtoRepoDto, ct)
                .ConfigureAwait(false);
            // SUPUESTO: la lista no trae "lastUpdate" (queda null salvo que el servidor lo incluya).
            return (IReadOnlyList<AzureRepo>)(dto?.Value ?? []).Select(r => new AzureRepo(r.Id ?? "", r.Name ?? "",
                r.DefaultBranch, r.RemoteUrl ?? "", r.Size, r.LastUpdate, r.Project?.Id)).ToList();
        });

    // El PAT nunca va en la URL; lo resuelve el credential helper de git.
    public static string CloneUrlFor(AzureRepo repo) => repo.RemoteUrl;

    public Task<IReadOnlyList<PullRequestInfo>> ListPullRequestsAsync(string? reviewerId = null, string? creatorId = null,
        CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var url = $"{Proj}/_apis/git/pullrequests?searchCriteria.status=active";
            if (reviewerId is not null) url += $"&searchCriteria.reviewerId={Uri.EscapeDataString(reviewerId)}";
            if (creatorId is not null) url += $"&searchCriteria.creatorId={Uri.EscapeDataString(creatorId)}";
            var dto = await GetJsonAsync($"{url}&{V}", AzureJsonContext.Default.ListDtoPrDto, ct).ConfigureAwait(false);
            return (IReadOnlyList<PullRequestInfo>)(dto?.Value ?? []).Select(MapPr).ToList();
        });

    public Task<PullRequestInfo> GetPullRequestAsync(string repoId, int id, CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var dto = await GetJsonAsync($"{Proj}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{id}?{V}",
                AzureJsonContext.Default.PrDto, ct).ConfigureAwait(false);
            return dto is null ? throw new ApiException(0, "Respuesta vacía de pullRequests.") : MapPr(dto);
        });

    // SUPUESTO: se cuenta la primera página de commits (por defecto 100 como máximo).
    public Task<int> GetCommitCountAsync(string repoId, int id, CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var dto = await GetJsonAsync(
                $"{Proj}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{id}/commits?{V}",
                AzureJsonContext.Default.ListDtoCommitRefDto, ct).ConfigureAwait(false);
            return dto?.Value?.Count ?? 0;
        });

    public Task VoteAsync(string repoId, int prId, string reviewerId, Vote vote, CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var url = $"{Proj}/_apis/git/repositories/{Uri.EscapeDataString(repoId)}/pullRequests/{prId}"
                      + $"/reviewers/{Uri.EscapeDataString(reviewerId)}?{V}";
            var body = new VoteBodyDto { Vote = (int)vote, Id = reviewerId };
            await SendJsonAsync(HttpMethod.Put, url, body, AzureJsonContext.Default.VoteBodyDto,
                AzureJsonContext.Default.ReviewerDto, ct).ConfigureAwait(false);
            return 0;
        });

    // SUPUESTO: endpoint de políticas en preview (7.1-preview.1); status viene como texto en camelCase.
    public Task<PolicySummary> GetPolicySummaryAsync(string projectId, int prId, CancellationToken ct = default) =>
        WithHint(CodeScopeHint, async () =>
        {
            var artifact = $"vstfs:///CodeReview/CodeReviewId/{projectId}/{prId}";
            var url = $"{Proj}/_apis/policy/evaluations?artifactId={Uri.EscapeDataString(artifact)}&{PolicyV}";
            var dto = await GetJsonAsync(url, AzureJsonContext.Default.ListDtoPolicyEvalDto, ct).ConfigureAwait(false);
            return PolicySummary.From((dto?.Value ?? []).Select(e => new PolicyEvaluation(
                e.Configuration?.Type?.DisplayName ?? "Política", ParsePolicyStatus(e.Status),
                e.Configuration?.IsBlocking ?? false)));
        });

    // SUPUESTO: pipelines/approvals admite state=pending y $expand=steps.
    public Task<IReadOnlyList<PipelineApproval>> ListPendingApprovalsAsync(CancellationToken ct = default) =>
        WithHint(BuildScopeHint, async () =>
        {
            var dto = await GetJsonAsync($"{Proj}/_apis/pipelines/approvals?state=pending&$expand=steps&{V}",
                AzureJsonContext.Default.ListDtoApprovalDto, ct).ConfigureAwait(false);
            return (IReadOnlyList<PipelineApproval>)(dto?.Value ?? []).Select(MapApproval).ToList();
        });

    // Aprobaciones donde el usuario es aprobador asignado. LIMITACIÓN: solo coincide por id directo o por
    // ids de grupos que el llamador entregue en memberOfIds; la API no resuelve la membresía por sí sola.
    public async Task<IReadOnlyList<PipelineApproval>> ListApprovalsForUserAsync(string userId,
        IReadOnlySet<string>? memberOfIds = null, CancellationToken ct = default)
    {
        var all = await ListPendingApprovalsAsync(ct).ConfigureAwait(false);
        return all.Where(a => a.Steps.Any(s => (s.Status is "pending" or "")
            && (Same(s.ApproverId, userId) || (memberOfIds?.Contains(s.ApproverId) ?? false)))).ToList();
    }

    public Task UpdateApprovalAsync(string approvalId, bool approve, string? comment = null,
        CancellationToken ct = default) =>
        WithHint(BuildScopeHint, async () =>
        {
            var body = new List<ApprovalUpdateDto>
            {
                new() { ApprovalId = approvalId, Status = approve ? "approved" : "rejected", Comment = comment },
            };
            using var _ = await SendAsync(() => new HttpRequestMessage(HttpMethod.Patch,
                $"{Proj}/_apis/pipelines/approvals?{V}")
            { Content = JsonContent.Create(body, AzureJsonContext.Default.ListApprovalUpdateDto) }, ct)
                .ConfigureAwait(false);
            return 0;
        });

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private PullRequestInfo MapPr(PrDto p)
    {
        var repo = p.Repository;
        var reviewers = (p.Reviewers ?? []).Select(r => new PullRequestReviewer(r.Id ?? "", r.DisplayName ?? "",
            VoteExtensions.FromInt(r.Vote), r.IsRequired, r.IsContainer)).ToList();
        var web = $"{Proj}/_git/{Uri.EscapeDataString(repo?.Name ?? "")}/pullrequest/{p.PullRequestId}";
        return new PullRequestInfo(p.PullRequestId, p.Title ?? "", p.Status ?? "", p.IsDraft,
            p.CreatedBy?.Id ?? "", p.CreatedBy?.DisplayName ?? "", p.CreationDate,
            p.SourceRefName ?? "", p.TargetRefName ?? "", repo?.Id ?? "", repo?.Name ?? "", repo?.Project?.Id,
            p.LastMergeSourceCommit?.CommitId, p.MergeStatus, reviewers, web) { Description = p.Description };
    }

    private static PipelineApproval MapApproval(ApprovalDto a) => new(a.Id ?? "", a.Status ?? "",
        a.Pipeline?.Name ?? "", a.Instructions, a.CreatedOn,
        (a.Steps ?? []).Select(s => new ApprovalStep(s.AssignedApprover?.Id ?? "",
            s.AssignedApprover?.DisplayName ?? "", s.Status ?? "", s.AssignedApprover?.IsContainer ?? false)).ToList());

    public static PolicyStatus ParsePolicyStatus(string? s) => s?.ToLowerInvariant() switch
    {
        "approved" => PolicyStatus.Approved,
        "rejected" => PolicyStatus.Rejected,
        "running" => PolicyStatus.Running,
        "queued" => PolicyStatus.Queued,
        "notapplicable" => PolicyStatus.NotApplicable,
        "broken" => PolicyStatus.Broken,
        _ => PolicyStatus.Unknown,
    };
}
