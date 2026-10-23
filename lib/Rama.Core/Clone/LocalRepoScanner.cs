namespace Rama.Core.Clone;

// Lee las URLs remotas de repos locales directamente de .git/config (sin lanzar git).
public static class LocalRepoScanner
{
    public static IReadOnlyList<string> ReadRemoteUrls(string repoPath)
    {
        try
        {
            var config = ResolveConfigPath(repoPath);
            return config is null ? [] : RemoteUrlMatcher.ParseRemoteUrls(File.ReadAllText(config));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // URL remota normalizada -> ruta local (gana la primera ruta encontrada).
    public static Dictionary<string, string> BuildIndex(IEnumerable<string> repoPaths)
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in repoPaths)
            foreach (var url in ReadRemoteUrls(path))
            {
                var key = RemoteUrlMatcher.Normalize(url);
                if (key.Length > 0) index.TryAdd(key, path);
            }
        return index;
    }

    private static string? ResolveConfigPath(string repoPath)
    {
        var dotGit = Path.Combine(repoPath, ".git");
        if (Directory.Exists(dotGit))
        {
            var cfg = Path.Combine(dotGit, "config");
            return File.Exists(cfg) ? cfg : null;
        }
        if (!File.Exists(dotGit)) return null;

        // Submódulo o worktree: ".git" es un archivo "gitdir: ruta".
        var line = File.ReadLines(dotGit).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
        if (line is null) return null;
        var gitDir = Path.GetFullPath(Path.Combine(repoPath, line["gitdir:".Length..].Trim()));
        var common = Path.Combine(gitDir, "commondir");
        if (File.Exists(common))
            gitDir = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(common).Trim()));
        var config = Path.Combine(gitDir, "config");
        return File.Exists(config) ? config : null;
    }
}
