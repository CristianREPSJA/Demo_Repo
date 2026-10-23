using System.Text.RegularExpressions;

namespace Rama.Core.Clone;

public sealed record GitProgress(string Phase, int? Percent, string? Transferred);

// Interpreta las líneas de progreso de `git clone --progress` (stderr).
public static partial class GitProgressParser
{
    private static readonly (string Git, string Label)[] Phases =
    [
        ("Enumerating objects", "Enumerando objetos"),
        ("Counting objects", "Contando objetos"),
        ("Compressing objects", "Comprimiendo objetos"),
        ("Receiving objects", "Recibiendo objetos"),
        ("Resolving deltas", "Resolviendo deltas"),
        ("Updating files", "Actualizando archivos"),
        ("Filtering content", "Filtrando contenido"),
    ];

    public static GitProgress? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var text = line.Trim();
        if (text.StartsWith("remote:", StringComparison.Ordinal)) text = text[7..].Trim();
        var m = Line().Match(text);
        if (!m.Success) return null;
        var label = Phases.FirstOrDefault(p => p.Git.Equals(m.Groups["phase"].Value, StringComparison.OrdinalIgnoreCase)).Label;
        if (label is null) return null;
        int? percent = m.Groups["pct"].Success ? int.Parse(m.Groups["pct"].Value) : null;
        var t = m.Groups["xfer"];
        return new GitProgress(label, percent, t.Success ? t.Value.Trim() : null);
    }

    [GeneratedRegex(@"^(?<phase>[A-Za-z ]+?):\s+(?:(?<pct>\d{1,3})%\s*)?(?:\([^)]*\))?(?:,\s*(?<xfer>[\d.]+\s*[KMGT]?i?B)(?:\s*\|.*)?)?")]
    private static partial Regex Line();
}
