namespace Rama.Core.Clone;

public static class CloneDestination
{
    private static readonly string[] RepoExtensions = [".git", ".bundle"];

    // Carpeta por defecto: {directorio de clonado}/{repo}; sin directorio base devuelve solo el nombre.
    public static string Default(string? cloneDir, string repoName)
    {
        var name = Sanitize(repoName);
        var dir = (cloneDir ?? "").Trim();
        if (dir.Length == 0) return name;
        var sep = dir.Contains('\\') && !dir.Contains('/') ? '\\' : '/';
        return dir.TrimEnd('\\', '/') + sep + name;
    }

    // Nombre de carpeta que git usaría para una URL (sin .git/.bundle).
    public static string NameFromUrl(string? url)
    {
        var raw = (url ?? "").Trim().TrimEnd('/', '\\');
        var i = raw.LastIndexOfAny(['/', '\\', ':']);
        var name = i >= 0 ? raw[(i + 1)..] : raw;
        foreach (var ext in RepoExtensions)
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length];
        return name;
    }

    public static string Sanitize(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        // Windows no admite nombres con punto/espacio final; solo puntos (".", "..") apuntarían a carpetas especiales.
        clean = clean.TrimEnd('.', ' ');
        return clean.Length == 0 ? "repo" : clean;
    }

    // Separa "dir/nombre" en carpeta padre y nombre local (lo que espera Commands.Clone).
    public static (string Parent, string Name) Split(string? destination)
    {
        var d = (destination ?? "").Trim().TrimEnd('\\', '/');
        var i = d.LastIndexOfAny(['\\', '/']);
        return i < 0 ? ("", d) : (d[..(i == 0 ? 1 : i)], d[(i + 1)..]);
    }
}
