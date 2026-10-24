namespace Rama.Core.Azure;

public enum Vote { Approved = 10, ApprovedWithSuggestions = 5, NoVote = 0, WaitingForAuthor = -5, Rejected = -10 }

public static class VoteExtensions
{
    public static Vote FromInt(int value) => Enum.IsDefined(typeof(Vote), value) ? (Vote)value : Vote.NoVote;

    public static string Label(this Vote vote) => vote switch
    {
        Vote.Approved => "Aprobó",
        Vote.ApprovedWithSuggestions => "Aprobó con sugerencias",
        Vote.WaitingForAuthor => "Esperando al autor",
        Vote.Rejected => "Rechazó",
        _ => "Sin votar",
    };
}

public sealed record AzureUser(string Id, string DisplayName);

public sealed record AzureRepo(string Id, string Name, string? DefaultBranch, string RemoteUrl, long? Size,
    DateTimeOffset? LastUpdate, string? ProjectId);

public sealed record PullRequestReviewer(string Id, string DisplayName, Vote Vote, bool IsRequired, bool IsGroup)
{
    public string VoteLabel => Vote.Label();
}

public sealed record PullRequestInfo(int Id, string Title, string Status, bool IsDraft, string CreatedById,
    string CreatedByName, DateTimeOffset? CreatedOn, string SourceBranch, string TargetBranch, string RepositoryId,
    string RepositoryName, string? ProjectId, string? SourceCommitId, string? MergeStatus,
    IReadOnlyList<PullRequestReviewer> Reviewers, string WebUrl)
{
    public string? Description { get; init; }
}

public enum PolicyStatus { Approved, Rejected, Running, Queued, NotApplicable, Broken, Unknown }

public sealed record PolicyEvaluation(string Name, PolicyStatus Status, bool IsBlocking);

public sealed record PolicySummary(IReadOnlyList<PolicyEvaluation> Items)
{
    private IEnumerable<PolicyEvaluation> Blocking => Items.Where(i => i.IsBlocking);

    // Políticas bloqueantes aún no cumplidas (pendientes, fallidas o rotas).
    public int Missing => Blocking.Count(i => i.Status is not (PolicyStatus.Approved or PolicyStatus.NotApplicable));
    public int Failed => Blocking.Count(i => i.Status is PolicyStatus.Rejected or PolicyStatus.Broken);
    public bool CanComplete => Missing == 0;
    public string Label => CanComplete ? "Listo para completar" : $"Faltan políticas ({Missing})";

    public static PolicySummary From(IEnumerable<PolicyEvaluation> items) => new(items.ToList());
}

public sealed record PipelineApproval(string Id, string Status, string PipelineName, string? Instructions,
    DateTimeOffset? CreatedOn, IReadOnlyList<ApprovalStep> Steps)
{
    // Id de la ejecución (build) dueña de la aprobación; null si la API no lo envía.
    public int? RunId { get; init; }
}

public sealed record ApprovalStep(string ApproverId, string ApproverName, string Status, bool IsGroup);

public enum PendingKind { PullRequestReview, PipelineApproval }

public sealed record PendingItem(string Id, PendingKind Kind, string Title, string Subtitle, string? WebUrl,
    DateTimeOffset? CreatedOn);
