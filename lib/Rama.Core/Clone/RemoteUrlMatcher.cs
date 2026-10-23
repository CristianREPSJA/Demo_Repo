using System.Text.RegularExpressions;

namespace Rama.Core.Clone;

// Detección de "Ya clonado": compara URLs remotas normalizadas (https/ssh, .git, mayúsculas, usuario@).
public static partial class RemoteUrlMatcher
{
    // Devuelve "host/ruta" en minúsculas, sin esquema, usuario, puerto, ".git" ni "/" final; "" si no es reconocible.
    public static string Normalize(string? url)
    {
        var raw = (url ?? "").Trim();
        if (raw.Length == 0) return "";

        string host, path;
        var scp = ScpLike().Match(raw);
        if (scp.Success && !raw.Contains("://", StringComparison.Ordinal))
        {
            host = scp.Groups["host"].Value;
            path = scp.Groups["path"].Value;
        }
        else if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
        {
            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
        }
        else return "";

        host = host.ToLowerInvariant();
        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];

        // SSH de Azure: ssh.dev.azure.com:v3/{org}/{proyecto}/{repo} equivale a dev.azure.com/{org}/{proyecto}/_git/{repo}.
        if (host == "ssh.dev.azure.com" && path.StartsWith("v3/", StringComparison.OrdinalIgnoreCase))
        {
            var p = path[3..].Split('/');
            if (p.Length == 3) { host = "dev.azure.com"; path = $"{p[0]}/{p[1]}/_git/{p[2]}"; }
        }
        return $"{host}/{path}".ToLowerInvariant();
    }

    public static bool AreSame(string? a, string? b)
    {
        var na = Normalize(a);
        return na.Length > 0 && na == Normalize(b);
    }

    // Extrae las URLs de [remote "x"] de un archivo .git/config.
    public static IReadOnlyList<string> ParseRemoteUrls(string? gitConfigText)
    {
        var urls = new List<string>();
        var inRemote = false;
        foreach (var line in (gitConfigText ?? "").Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith('['))
            {
                inRemote = t.StartsWith("[remote ", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inRemote) continue;
            var m = UrlLine().Match(t);
            if (m.Success) urls.Add(m.Groups[1].Value.Trim());
        }
        return urls;
    }

    public static ISet<string> NormalizedSet(IEnumerable<string> urls) =>
        urls.Select(Normalize).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[^:/\s]+):(?<path>[^/].*)$")]
    private static partial Regex ScpLike();

    [GeneratedRegex(@"^url\s*=\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlLine();
}
