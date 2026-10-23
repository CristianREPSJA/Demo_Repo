using System.Text;

namespace Rama.Core.Conflicts;

// Parser de marcadores de conflicto de git (tamaño 7). Preserva el texto byte a byte (CRLF/LF).
// Reglas: un marcador es exactamente 7 caracteres seguidos de espacio o fin de línea ('=======' exacto);
// dentro de "ours" solo '|||||||' y '=======' cortan; dentro de "theirs" solo '>>>>>>>' cierra.
// Una región sin cerrar (sin separador o fin) se deja como texto común.
public static class ConflictParser
{
    private const int MarkerSize = 7;

    public static ConflictDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = SplitLines(text);
        var segments = new List<DocumentSegment>();
        var common = new StringBuilder();
        var i = 0;
        var hunkCount = 0;

        while (i < lines.Count)
        {
            if (IsMarker(lines[i], '<') && TryParseHunk(lines, i, hunkCount, out var hunk, out var next))
            {
                FlushCommon(common, segments);
                segments.Add(hunk);
                hunkCount++;
                i = next;
                continue;
            }

            common.Append(lines[i]);
            i++;
        }

        FlushCommon(common, segments);
        return new ConflictDocument(segments);
    }

    // Separa en líneas conservando el '\n' (y el '\r' previo) de cada una.
    internal static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var nl = text.IndexOf('\n', start);
            var end = nl < 0 ? text.Length : nl + 1;
            lines.Add(text[start..end]);
            start = end;
        }
        return lines;
    }

    internal static bool IsMarker(string line, char c)
    {
        var content = line.TrimEnd('\n').TrimEnd('\r');
        if (content.Length < MarkerSize)
            return false;

        for (var k = 0; k < MarkerSize; k++)
        {
            if (content[k] != c)
                return false;
        }

        if (c == '=')
            return content.Length == MarkerSize;

        return content.Length == MarkerSize || content[MarkerSize] == ' ';
    }

    private static bool TryParseHunk(List<string> lines, int start, int index, out ConflictHunk hunk, out int next)
    {
        hunk = null!;
        next = start;

        var j = start + 1;
        var ours = Collect(lines, ref j, l => IsMarker(l, '|') || IsMarker(l, '='));
        if (j >= lines.Count)
            return false;

        string? baseMarker = null;
        string? baseText = null;
        if (IsMarker(lines[j], '|'))
        {
            baseMarker = lines[j++];
            baseText = Collect(lines, ref j, l => IsMarker(l, '='));
            if (j >= lines.Count)
                return false;
        }

        var separator = lines[j++];
        var theirs = Collect(lines, ref j, l => IsMarker(l, '>'));
        if (j >= lines.Count)
            return false;

        hunk = new ConflictHunk(index, lines[start], ours, baseMarker, baseText, separator, theirs, lines[j]);
        next = j + 1;
        return true;
    }

    private static string Collect(List<string> lines, ref int j, Func<string, bool> stop)
    {
        var sb = new StringBuilder();
        while (j < lines.Count && !stop(lines[j]))
            sb.Append(lines[j++]);
        return sb.ToString();
    }

    private static void FlushCommon(StringBuilder common, List<DocumentSegment> segments)
    {
        if (common.Length == 0)
            return;

        segments.Add(new CommonSegment(common.ToString()));
        common.Clear();
    }
}
