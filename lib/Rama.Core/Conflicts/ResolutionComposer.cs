using System.Text;

namespace Rama.Core.Conflicts;

// Ubicación de un segmento del documento dentro del texto compuesto. ConflictIndex = -1 para texto común.
public sealed record ComposedSpan(int SegmentIndex, int ConflictIndex, int Start, int Length, bool IsResolved);

public sealed record ComposeResult(string Text, int UnresolvedCount, IReadOnlyList<ComposedSpan> Spans);

public static class ResolutionComposer
{
    public static ComposeResult Compose(ConflictDocument document) => Compose(document, []);

    // Compone el resultado; regiones sin elección (o con menos elecciones que regiones) quedan con marcadores.
    public static ComposeResult Compose(ConflictDocument document, IReadOnlyList<RegionResolution> choices)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(choices);
        if (choices.Count > document.ConflictCount)
            throw new ArgumentException("Hay más elecciones que regiones en conflicto.", nameof(choices));

        var sb = new StringBuilder();
        var spans = new List<ComposedSpan>(document.Segments.Count);
        var unresolved = 0;

        for (var s = 0; s < document.Segments.Count; s++)
        {
            var start = sb.Length;
            if (document.Segments[s] is ConflictHunk hunk)
            {
                var choice = hunk.Index < choices.Count ? choices[hunk.Index] : RegionResolution.Unresolved;
                var resolved = IsResolved(choice);
                if (!resolved)
                    unresolved++;

                sb.Append(Render(hunk, choice));
                spans.Add(new ComposedSpan(s, hunk.Index, start, sb.Length - start, resolved));
            }
            else
            {
                sb.Append(document.Segments[s].OriginalText);
                spans.Add(new ComposedSpan(s, -1, start, sb.Length - start, true));
            }
        }

        return new ComposeResult(sb.ToString(), unresolved, spans);
    }

    public static string Render(ConflictHunk hunk, RegionResolution choice)
    {
        var text = choice.Kind switch
        {
            ResolutionKind.Ours => hunk.Ours,
            ResolutionKind.Theirs => hunk.Theirs,
            ResolutionKind.BothOursFirst => hunk.Ours + hunk.Theirs,
            ResolutionKind.BothTheirsFirst => hunk.Theirs + hunk.Ours,
            ResolutionKind.Manual => choice.ManualText ?? string.Empty,
            _ => hunk.OriginalText,
        };

        // Si el archivo terminaba sin salto de línea tras el marcador, el resultado tampoco lo lleva.
        if (choice.Kind is not (ResolutionKind.Manual or ResolutionKind.Unresolved) && hunk.EndsAtEofWithoutNewline)
            text = TrimOneTerminator(text);

        return text;
    }

    private static bool IsResolved(RegionResolution choice) => choice.Kind switch
    {
        ResolutionKind.Unresolved => false,
        ResolutionKind.Manual => ConflictParser.Parse(choice.ManualText ?? string.Empty).ConflictCount == 0,
        _ => true,
    };

    private static string TrimOneTerminator(string text)
    {
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
            return text[..^2];
        return text.EndsWith('\n') ? text[..^1] : text;
    }
}
