using Rama.Core.Azure;

namespace Rama.Core.Reviews;

public enum ReviewKind { PullRequest, Deployment, MyPullRequest }

public enum Tone { Neutral, Good, Warn, Bad }

public sealed record ReviewListItem(string Key, ReviewKind Kind, string Title, string Number, string Author,
    string Initials, string Meta, DateTimeOffset? CreatedOn, string? Pill, Tone PillTone,
    PullRequestInfo? Pr, PipelineApproval? Approval);

public sealed record ReviewsSnapshot(AzureUser Me, IReadOnlyList<ReviewListItem> PullRequests,
    IReadOnlyList<ReviewListItem> Deployments, IReadOnlyList<ReviewListItem> MyPullRequests, DateTimeOffset FetchedAt)
{
    public int PendingCount => PullRequests.Count + Deployments.Count;
    public IEnumerable<ReviewListItem> Pending => PullRequests.Concat(Deployments);
}

public sealed record PolicyRow(string Name, string StatusText, Tone Tone);

public sealed record ReviewerRow(string Name, string Initials, string VoteLabel, Tone Tone, bool IsMe, bool IsRequired);

public sealed record PullRequestDetail(PullRequestInfo Pr, int CommitCount, string CommitsText, string Source,
    string Target, IReadOnlyList<PolicyRow> Policies, IReadOnlyList<ReviewerRow> Reviewers, string? CompleteBlockReason)
{
    public bool CanComplete => CompleteBlockReason is null;
}

public sealed record VoteOption(Vote Vote, string Label, bool NeedsConfirm);
