namespace Rama.Core.Bitbucket;

public sealed record BitbucketWorkspace(string Slug, string Name);

public sealed record BitbucketRepo(
    string Name,
    string Slug,
    string FullName,
    string? Description,
    string? ProjectName,
    string? MainBranch,
    DateTimeOffset? UpdatedOn,
    long Size,
    bool IsPrivate,
    string? HttpsCloneUrl);

public sealed record BitbucketUser(string DisplayName, string? AccountId);
