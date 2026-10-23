namespace Rama.Core.Terminal;

// Rama base para "Revisar rama contra ...": develop si existe; si no, la rama por defecto (main/master).
public static class BaseBranchResolver
{
    private static readonly string[] Candidates = ["develop", "main", "master"];

    // localBranches: nombres locales; remoteBranches: "origin/x". Prefiere local sobre remota.
    public static string? Resolve(IEnumerable<string> localBranches, IEnumerable<string> remoteBranches, string? currentBranch)
    {
        ArgumentNullException.ThrowIfNull(localBranches);
        ArgumentNullException.ThrowIfNull(remoteBranches);
        var local = localBranches.ToHashSet(StringComparer.Ordinal);
        var remote = remoteBranches.ToList();

        foreach (var name in Candidates)
        {
            if (name == currentBranch)
                continue;
            if (local.Contains(name))
                return name;
            var remoteHit = remote.FirstOrDefault(r => r.EndsWith("/" + name, StringComparison.Ordinal));
            if (remoteHit != null)
                return remoteHit;
        }
        return null;
    }
}
