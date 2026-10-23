namespace Rama.Core.Conflicts;

public sealed record EditOutcome(ConflictDocument Document, IReadOnlyList<RegionResolution> Choices, bool Reparsed);

// Traduce una edición del panel Resultado a documento + elecciones:
// dentro de una región => Manual; dentro de texto común => se actualiza ese texto;
// si cruza segmentos => se reanaliza todo el texto (las regiones resueltas pasan a ser texto común).
public static class ResultEdit
{
    public static EditOutcome Apply(
        ConflictDocument document,
        IReadOnlyList<RegionResolution> choices,
        ComposeResult before,
        int offset,
        int removedLength,
        string newText)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(newText);
        if (offset < 0 || removedLength < 0 || offset + removedLength > before.Text.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var inserted = newText.Length - before.Text.Length + removedLength;
        var span = FindSpan(before.Spans, offset, removedLength);
        if (span is null || inserted < 0)
            return Reparse(newText);

        var segmentText = newText.Substring(span.Start, span.Length - removedLength + inserted);
        if (span.ConflictIndex < 0)
            return new EditOutcome(document.WithCommonText(span.SegmentIndex, segmentText), Normalize(choices, document), false);

        var updated = Normalize(choices, document);
        updated[span.ConflictIndex] = RegionResolution.Manual(segmentText);
        return new EditOutcome(document, updated, false);
    }

    // Prefiere la región en conflicto cuando la edición toca un límite.
    private static ComposedSpan? FindSpan(IReadOnlyList<ComposedSpan> spans, int offset, int removed)
    {
        ComposedSpan? common = null;
        foreach (var s in spans)
        {
            if (offset < s.Start || offset + removed > s.Start + s.Length)
                continue;
            if (s.ConflictIndex >= 0)
                return s;
            common ??= s;
        }
        return common;
    }

    private static RegionResolution[] Normalize(IReadOnlyList<RegionResolution> choices, ConflictDocument document)
    {
        var result = new RegionResolution[document.ConflictCount];
        for (var i = 0; i < result.Length; i++)
            result[i] = i < choices.Count ? choices[i] : RegionResolution.Unresolved;
        return result;
    }

    private static EditOutcome Reparse(string text)
    {
        var doc = ConflictParser.Parse(text);
        return new EditOutcome(doc, Normalize([], doc), true);
    }
}
