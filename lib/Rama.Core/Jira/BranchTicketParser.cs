using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rama.Core.Jira;

public static partial class BranchTicketParser
{
    public const int MaxSlugLength = 40;

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z][A-Z0-9]+-\d+(?![0-9])")]
    private static partial Regex KeyPattern();

    // feature/VEN-412-x -> VEN-412; null si no hay clave.
    public static string? ExtractKey(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch)) return null;
        var m = KeyPattern().Match(branch);
        return m.Success ? m.Value : null;
    }

    public static string BranchNameFor(JiraIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        var slug = Slugify(issue.Summary);
        return slug.Length == 0 ? $"feature/{issue.Key}" : $"feature/{issue.Key}-{slug}";
    }

    public static string Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var sb = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var l = char.ToLowerInvariant(c);
            if (l is (>= 'a' and <= 'z') or (>= '0' and <= '9')) sb.Append(l);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxSlugLength) slug = slug[..MaxSlugLength].TrimEnd('-');
        return slug;
    }
}
