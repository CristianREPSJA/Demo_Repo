using Rama.Core.Azure;
using Rama.Core.Bitbucket;

namespace Rama.Core.Clone;

public enum CloneProvider { Azure, Bitbucket }

// Fila de la tabla de clonado, independiente del proveedor.
public sealed record CloneRepoItem(
    CloneProvider Provider,
    string Name,
    string? Description,
    string Project,
    string? DefaultBranch,
    DateTimeOffset? UpdatedOn,
    long? SizeBytes,
    string CloneUrl)
{
    public static CloneRepoItem From(AzureRepo repo, string project) => new(CloneProvider.Azure, repo.Name, null,
        project, ShortBranch(repo.DefaultBranch), repo.LastUpdate, repo.Size, AzureDevOpsClient.CloneUrlFor(repo));

    public static CloneRepoItem From(BitbucketRepo repo) => new(CloneProvider.Bitbucket, repo.Name, repo.Description,
        repo.ProjectName ?? "", repo.MainBranch, repo.UpdatedOn, repo.Size > 0 ? repo.Size : null,
        BitbucketClient.CloneUrlFor(repo));

    private static string? ShortBranch(string? refName) =>
        refName?.StartsWith("refs/heads/", StringComparison.Ordinal) == true ? refName["refs/heads/".Length..] : refName;
}
